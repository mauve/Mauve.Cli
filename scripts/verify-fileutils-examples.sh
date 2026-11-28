#!/usr/bin/env bash
set -euo pipefail
ulimit -c 0

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

if (( $# < 1 || $# > 3 )); then
    echo "Usage: $0 EVIDENCE_DIRECTORY [COMPILER=clang] [--managed-smoke]" >&2
    exit 1
fi

compiler="${2:-clang}"
smoke="${3:-}"
if [[ -n "$smoke" && "$smoke" != "--managed-smoke" ]]; then
    echo "The optional third argument must be --managed-smoke." >&2
    exit 1
fi

export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}"

evidence="$(realpath -m "$1")/fileutils-examples"
case "$evidence" in
    "$root"/*) ;;
    *)
        echo "The evidence directory must be inside the repository." >&2
        exit 1
        ;;
esac
if [[ -e "$evidence" ]]; then
    echo "FileUtilities example evidence already exists: $evidence" >&2
    exit 1
fi

rid="linux-x64"
host_rid="$(dotnet --info | awk '/^[[:space:]]*RID:/ { print $2; exit }')"
if [[ "$host_rid" != "$rid" || "$(uname -m)" != "x86_64" ]]; then
    echo "FileUtilities example verification requires a native linux-x64 host." >&2
    exit 1
fi
if [[ -z "$smoke" ]]; then
    command -v "$compiler" >/dev/null
fi
mkdir -p "$evidence"

fixture_root="$(mktemp -d "${TMPDIR:-/tmp}/mauve-fileutils.XXXXXX")"
touch "$fixture_root/.fileutils-example-fixture"
cleanup_fixtures() {
    if [[ -d "$fixture_root" && -f "$fixture_root/.fileutils-example-fixture" ]]; then
        rm -rf -- "$fixture_root"
    fi
}
trap cleanup_fixtures EXIT

{
    echo "UTC: $(date -u +%FT%TZ)"
    echo "RID: $rid"
    echo "Compiler: $compiler"
    echo "Mode: ${smoke:-trimmed-and-native}"
    echo "Commit: $(git rev-parse HEAD)"
    echo "Working tree:"
    git status --short
    echo "Tracked diff SHA-256:"
    git diff --binary | sha256sum
    echo "SDK:"
    dotnet --info
    if [[ -z "$smoke" ]]; then
        echo "Compiler version:"
        "$compiler" --version
    fi
} >"$evidence/environment.txt" 2>&1

{
    sha256sum scripts/verify-fileutils-examples.sh scripts/verify-aot.sh \
        Mauve.Cli.slnx README.md examples/README.md \
        examples/00-FileUtilities/README.md
    find examples/00-FileUtilities \
        -type d \( -name bin -o -name obj \) -prune -o \
        -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \
        -o -name 'packages.lock.json' -o -name '*.md' \) \
        -print0 | sort -z | xargs -0 sha256sum
} >"$evidence/source-sha256.txt"

run_logged() {
    local log="$1"
    shift
    {
        printf '$'
        printf ' %q' "$@"
        printf '\n'
    } | tee -a "$log"
    "$@" 2>&1 | tee -a "$log"
}

run_case() {
    local log="$1"
    local case_name="$2"
    local expected="$3"
    shift 3
    local prefix="$evidence/$case_name"
    local status
    {
        printf '$'
        printf ' %q' "$@"
        printf '\n'
    } | tee -a "$log"
    if "$@" >"$prefix.stdout" 2>"$prefix.stderr"; then
        status=0
    else
        status=$?
    fi
    {
        printf 'Expected exit: %s; actual exit: %s\n' "$expected" "$status"
        echo "stdout:"
        cat "$prefix.stdout"
        echo "stderr:"
        cat "$prefix.stderr"
    } >>"$log"
    if [[ "$expected" == "nonzero" ]]; then
        if [[ "$status" == 0 || ! -s "$prefix.stderr" ]]; then
            echo "Expected a reported failure for $case_name; see $log." >&2
            exit 1
        fi
    elif [[ "$status" != "$expected" ]]; then
        echo "Unexpected exit code for $case_name; see $log." >&2
        exit 1
    fi
    if [[ "$status" == 0 && -s "$prefix.stderr" ]]; then
        echo "Unexpected stderr for $case_name; see $log." >&2
        exit 1
    fi
    if [[ "$status" != 0 && "$status" != 1 && ! -s "$prefix.stderr" ]]; then
        echo "Expected a diagnostic for $case_name; see $log." >&2
        exit 1
    fi
    if [[ "$status" == 1 && ! -s "$prefix.stdout" && ! -s "$prefix.stderr" ]]; then
        echo "Expected output for the nonzero result of $case_name; see $log." >&2
        exit 1
    fi
}

verify_application() {
    local log="$1"
    local executable="$2"
    local output_directory="$3"
    local label="$4"
    local source="$fixture_root/source.txt"
    local different="$fixture_root/different.txt"
    local destination="$fixture_root/destination.txt"
    local actual
    local link

    printf 'alpha\n' >"$source"
    printf 'bravo\n' >"$different"
    rm -f -- "$destination"

    run_case "$log" "$label-help-tree" 0 "$executable" --help-tree
    grep -Fq 'copy' "$evidence/$label-help-tree.stdout"
    grep -Fq 'inspect' "$evidence/$label-help-tree.stdout"
    grep -Fq 'compare' "$evidence/$label-help-tree.stdout"

    run_case "$log" "$label-dry-run" 0 "$executable" copy "$source" "$destination" --dry-run
    if [[ -e "$destination" ]]; then
        echo "Dry-run created a destination: $label" >&2
        exit 1
    fi
    run_case "$log" "$label-copy-alias" 0 "$executable" cp \
        "$source" "$fixture_root/alias-copy.txt" -n
    if [[ -e "$fixture_root/alias-copy.txt" ]]; then
        echo "The copy alias did not preserve dry-run behavior: $label" >&2
        exit 1
    fi

    run_case "$log" "$label-copy" 0 "$executable" copy "$source" "$destination"
    if ! cmp -s "$source" "$destination"; then
        echo "Copy output did not match its source: $label" >&2
        exit 1
    fi

    printf 'preserve me\n' >"$destination"
    run_case "$log" "$label-combined-short-flags" 0 \
        "$executable" copy "$source" "$destination" -nf
    if [[ "$(cat "$destination")" != "preserve me" ]]; then
        echo "Combined short flags changed the destination during dry-run: $label" >&2
        exit 1
    fi
    run_case "$log" "$label-overwrite-protected" nonzero \
        "$executable" copy "$source" "$destination"
    if [[ "$(cat "$destination")" != "preserve me" ]]; then
        echo "Rejected overwrite changed the destination: $label" >&2
        exit 1
    fi
    run_case "$log" "$label-overwrite" 0 \
        "$executable" copy "$source" "$destination" -f
    if ! cmp -s "$source" "$destination"; then
        echo "Explicit overwrite did not copy the source: $label" >&2
        exit 1
    fi
    run_case "$log" "$label-same-path" 74 "$executable" copy "$source" "$source" -f
    if [[ "$(cat "$source")" != "alpha" ]]; then
        echo "Same-path copy changed its source: $label" >&2
        exit 1
    fi

    run_case "$log" "$label-inspect" 0 "$executable" inspect "$source"
    if [[ ! -s "$evidence/$label-inspect.stdout" ]]; then
        echo "Inspect produced no metadata: $label" >&2
        exit 1
    fi
    run_case "$log" "$label-inspect-alias" 0 "$executable" stat "$source"
    run_case "$log" "$label-missing-file" 66 "$executable" inspect "$fixture_root/missing.txt"
    grep -Fq 'The requested file was not found.' "$evidence/$label-missing-file.stderr"
    run_case "$log" "$label-compare-equal" 0 "$executable" compare "$source" "$destination"
    run_case "$log" "$label-compare-alias" 0 "$executable" cmp "$source" "$destination"
    run_case "$log" "$label-compare-different" 1 "$executable" compare "$source" "$different"
    run_case "$log" "$label-invalid-buffer-size" 2 "$executable" copy \
        "$source" "$fixture_root/invalid-copy.txt" --buffer-size 0B --dry-run

    run_case "$log" "$label-default-origin" 0 env -u FILEUTILS_BUFFER_SIZE \
        "$executable" settings
    grep -Fq -- '--buffer-size: Default' "$evidence/$label-default-origin.stdout"
    run_case "$log" "$label-environment-origin" 0 env FILEUTILS_BUFFER_SIZE=8KiB \
        "$executable" settings
    grep -Fq -- '--buffer-size: Environment' "$evidence/$label-environment-origin.stdout"
    run_case "$log" "$label-explicit-origin" 0 env FILEUTILS_BUFFER_SIZE=8KiB \
        "$executable" settings --buffer-size 16KiB
    grep -Fq -- '--buffer-size: Explicit' "$evidence/$label-explicit-origin.stdout"
    run_case "$log" "$label-custom-directive" 0 "$executable" '[about]'
    if [[ ! -s "$evidence/$label-custom-directive.stdout" ]]; then
        echo "The about directive produced no output: $label" >&2
        exit 1
    fi
    run_case "$log" "$label-completion" 0 "$executable" '[completion:bash]'
    if [[ ! -s "$evidence/$label-completion.stdout" ]]; then
        echo "Shell completion registration produced no output: $label" >&2
        exit 1
    fi
    run_case "$log" "$label-value-completion" 0 "$executable" --demo-completion
    grep -Fq '4KiB' "$evidence/$label-value-completion.stdout"
    grep -Fq '64KiB' "$evidence/$label-value-completion.stdout"
    grep -Fq '1MiB' "$evidence/$label-value-completion.stdout"

    for link in mcopy minspect mcompare; do
        ln -s "$executable" "$output_directory/$link"
    done
    run_case "$log" "$label-mcopy" 0 "$output_directory/mcopy" \
        "$source" "$fixture_root/multicall-copy.txt" --dry-run
    run_case "$log" "$label-minspect" 0 "$output_directory/minspect" "$source"
    run_case "$log" "$label-mcompare" 0 "$output_directory/mcompare" "$source" "$destination"

    if find "$output_directory" -iname '*Mauve.Cli.Generators*' -print -quit | grep -q .; then
        echo "The FileUtilities publication contains the build-time generator: $label" >&2
        exit 1
    fi
    sha256sum "$executable" | tee -a "$log"
    actual="$(stat --printf='%s' "$source")"
    printf 'Fixture source bytes: %s\n' "$actual" >>"$log"
}

verify_variant_parity() {
    local mode="$1"
    local case_name
    local stream
    for case_name in \
        dry-run copy-alias copy combined-short-flags overwrite-protected overwrite same-path \
        inspect inspect-alias missing-file compare-equal compare-alias compare-different \
        invalid-buffer-size default-origin environment-origin explicit-origin custom-directive \
        completion value-completion mcopy minspect mcompare; do
        for stream in stdout stderr; do
            local declarative="$evidence/FileUtilities.Declarative-$mode-$case_name.$stream"
            local manual="$evidence/FileUtilities.Manual-$mode-$case_name.$stream"
            if ! cmp -s "$declarative" "$manual"; then
                diff -u "$declarative" "$manual" >&2 || true
                echo "Declarative/Manual $mode output differs for $case_name." >&2
                exit 1
            fi
        done
    done
}

for variant in Declarative Manual; do
    project="examples/00-FileUtilities/$variant/FileUtilities.$variant.csproj"
    example="FileUtilities.$variant"
    binary="fileutils-${variant,,}"
    log="$evidence/$example.log"

    run_logged "$log" dotnet restore "$project" --locked-mode
    run_logged "$log" dotnet build "$project" -c Release --no-restore \
        -p:PublishAot=false -p:TreatWarningsAsErrors=true \
        -p:CodeAnalysisTreatWarningsAsErrors=true

    managed="$evidence/$example-managed"
    run_logged "$log" dotnet publish "$project" -c Release --no-restore \
        -p:PublishAot=false -p:PublishTrimmed=false -p:SelfContained=false \
        --output "$managed"
    verify_application "$log" "$managed/$binary" "$managed" "$example-managed"
    if [[ -n "$smoke" ]]; then
        continue
    fi

    trimmed="$evidence/$example-trimmed"
    run_logged "$log" dotnet publish "$project" -c Release -r "$rid" --no-restore \
        -p:BuildProjectReferences=false --output "$trimmed" \
        -p:PublishAot=false -p:PublishTrimmed=true -p:TrimMode=link \
        -p:SelfContained=true -p:EnableTrimAnalyzer=true \
        -p:ILLinkTreatWarningsAsErrors=true
    verify_application "$log" "$trimmed/$binary" "$trimmed" "$example-trimmed"

    native="$evidence/$example-native"
    run_logged "$log" dotnet publish "$project" -c Release -r "$rid" --no-restore \
        -p:BuildProjectReferences=false --output "$native" \
        -p:PublishAot=true -p:PublishTrimmed=true -p:EnableTrimAnalyzer=true \
        -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true \
        "-p:CppCompilerAndLinker=$compiler"
    verify_application "$log" "$native/$binary" "$native" "$example-native"
done

verify_variant_parity managed
if [[ -z "$smoke" ]]; then
    verify_variant_parity trimmed
    verify_variant_parity native
fi

if [[ -n "$smoke" ]]; then
    echo "Managed-only FileUtilities example smokes verified; trimmed/native publication not run." \
        | tee "$evidence/result.txt"
else
    echo "Both FileUtilities variants passed managed, trimmed, and NativeAOT process checks." \
        | tee "$evidence/result.txt"
fi
