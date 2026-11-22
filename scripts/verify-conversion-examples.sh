#!/usr/bin/env bash
set -euo pipefail

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

# Retain each run independently rather than deleting an earlier evidence set.
evidence="$(realpath -m "$1")/conversion-examples"
case "$evidence" in
    "$root"/*) ;;
    *)
        echo "The evidence directory must be inside the repository." >&2
        exit 1
        ;;
esac
if [[ -e "$evidence" ]]; then
    echo "Conversion example evidence already exists: $evidence" >&2
    exit 1
fi

rid="linux-x64"
host_rid="$(dotnet --info | awk '/^[[:space:]]*RID:/ { print $2; exit }')"
if [[ "$host_rid" != "$rid" || "$(uname -m)" != "x86_64" ]]; then
    echo "Conversion example verification requires a native linux-x64 host." >&2
    exit 1
fi
if [[ -z "$smoke" ]]; then
    command -v "$compiler" >/dev/null
fi
mkdir -p "$evidence"

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
    sha256sum scripts/verify-conversion-examples.sh \
        Directory.Build.props Directory.Packages.props global.json NuGet.Config \
        .editorconfig examples/.editorconfig
    find src/Mauve.Cli src/Mauve.Cli.Generators \
        examples/12-BuiltInTypes examples/13-CustomConverters \
        -type d \( -name bin -o -name obj \) -prune -o \
        -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \
        -o -name 'packages.lock.json' -o -name '.editorconfig' -o -name 'README.md' \) \
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

verify_case() {
    local label="$1"
    local name="$2"
    local expected_status="$3"
    local expected_stdout="$4"
    shift 4
    local prefix="$evidence/$label-$name"
    local status
    {
        printf '$'
        printf ' %q' "$@"
        printf '\n'
    } | tee -a "$evidence/$label.log"
    if "$@" >"$prefix.stdout" 2>"$prefix.stderr"; then
        status=0
    else
        status=$?
    fi
    if [[ -n "$expected_stdout" ]]; then
        printf '%s\n' "$expected_stdout" >"$prefix.expected-stdout"
    else
        : >"$prefix.expected-stdout"
    fi
    {
        printf 'Expected exit: %s; actual exit: %s\n' "$expected_status" "$status"
        echo "stdout:"
        cat "$prefix.stdout"
        echo "stderr:"
        cat "$prefix.stderr"
    } >>"$evidence/$label.log"
    if [[ "$status" != "$expected_status" ]] \
        || ! cmp -s "$prefix.expected-stdout" "$prefix.stdout"; then
        echo "Conversion example output/exit mismatch: $label/$name" >&2
        cat "$prefix.stdout" "$prefix.stderr" >&2
        exit 1
    fi
    if [[ "$expected_status" == 0 && -s "$prefix.stderr" ]] \
        || [[ "$expected_status" != 0 && ! -s "$prefix.stderr" ]]; then
        echo "Conversion example diagnostic mismatch: $label/$name" >&2
        exit 1
    fi
    printf 'Passed process smoke: %s/%s (exit %s)\n' "$label" "$name" "$status" \
        | tee -a "$evidence/$label.log"
}

verify_built_ins() {
    local label="$1"
    shift
    verify_case "$label" numbers 0 \
        $'signed-byte=-8\nunsigned-byte=8\nsigned-short=-16\nunsigned-short=16\nsigned-int=-32\nunsigned-int=32\nsigned-long=-64\nunsigned-long=64\nhalf=1.5\nsingle=2.5\ndouble=3.5\ndecimal=4.5\nenabled=True' \
        "$@" numbers --signed-byte -8 --unsigned-byte 8 \
        --signed-short -16 --unsigned-short 16 --signed-int -32 --unsigned-int 32 \
        --signed-long -64 --unsigned-long 64 \
        --half 1.5 --single 2.5 --double 3.5 --decimal 4.5 --enabled true
    verify_case "$label" identity 0 \
        $'text=hello\nletter=Q\nid=01234567-89ab-cdef-0123-456789abcdef\nuri=https://example.invalid/demo\nversion=1.2.3\naddress=192.0.2.1\nfile=demo.txt\ndirectory=demo-folder\nmode=Detailed' \
        "$@" identity --text hello --letter Q --id 01234567-89ab-cdef-0123-456789abcdef \
        --uri https://example.invalid/demo --version 1.2.3 --address 192.0.2.1 \
        --file demo.txt --directory demo-folder --mode Detailed
    verify_case "$label" time 0 \
        $'date=2026-11-22\ntime=16:30:45.1230000\ntimestamp=2026-11-22T16:30:45.0000000+00:00\nduration=1.02:03:04' \
        "$@" time --date 2026-11-22 --time 16:30:45.123 \
        --timestamp 2026-11-22T16:30:45Z --duration 1.02:03:04
    verify_case "$label" shapes-null 0 \
        $'maybe-date=(null)\nsample=1.5\nsample=2.5\ntarget=port:8080\nroute=port:443\nroute=name:service' \
        "$@" shapes --maybe-date --sample 1.5 --sample 2.5 \
        --target 8080 --route 443 --route service
    verify_case "$label" shapes-absent 0 \
        $'maybe-date=(absent)\nsample=1.5\ntarget=name:service\nroute=port:443' \
        "$@" shapes --sample 1.5 --target service --route 443
    verify_case "$label" shapes-value 0 \
        $'maybe-date=2026-11-22\nsample=1.5\ntarget=name:service\nroute=port:443' \
        "$@" shapes --maybe-date=2026-11-22 --sample 1.5 --target service --route 443
    verify_case "$label" invalid-date 2 "" \
        "$@" time --date 2026-02-30 --time 16:30 \
        --timestamp 2026-11-22T16:30:45Z --duration 00:01:00
}

verify_custom() {
    local label="$1"
    shift
    verify_case "$label" directive 0 \
        $'audit: code=7; mask=15\ncode=42; mask=255; count=10; input=9' \
        "$@" '[audit:B-7' '0x0F]' --code B-42 --mask 0xFF --count 10 B-9
    verify_case "$label" command 0 'code=42; mask=255; count=10; input=9' \
        "$@" --code B-42 --mask 0xFF --count 10 B-9
    verify_case "$label" invalid-code 2 "" \
        "$@" --code B-bad --mask 0xFF --count 10 B-9
    verify_case "$label" invalid-mask 2 "" \
        "$@" --code B-42 --mask 0xGG --count 10 B-9
    verify_case "$label" local-override 2 "" \
        "$@" --code B-42 --mask 0xFF --count 0x10 B-9
    verify_case "$label" invalid-positional 2 "" \
        "$@" --code B-42 --mask 0xFF --count 10 B-bad
    verify_case "$label" invalid-directive 2 "" \
        "$@" '[audit:B-7' '0xGG]' --code B-42 --mask 0xFF --count 10 B-9
}

verify_outputs() {
    local label="$1"
    local kind="$2"
    shift 2
    if [[ "$kind" == "BuiltInTypes" ]]; then
        verify_built_ins "$label" "$@"
    else
        verify_custom "$label" "$@"
    fi
}

hash_outputs() {
    local label="$1"
    local directory="$2"
    if find "$directory" -iname '*Mauve.Cli.Generators*' -print -quit | grep -q .; then
        echo "Runtime output contains the build-time generator: $label" >&2
        exit 1
    fi
    find "$directory" -type f -print0 | sort -z | xargs -0 sha256sum \
        >"$evidence/$label-output-sha256.txt"
}

for folder in 12-BuiltInTypes 13-CustomConverters; do
    kind="${folder#*-}"
    for variant in Declarative Manual; do
        example="$kind.$variant"
        project="examples/$folder/$variant/$example.csproj"
        log="$evidence/$example.log"
        run_logged "$log" dotnet restore "$project" --locked-mode
        run_logged "$log" dotnet build "$project" --configuration Release --no-restore \
            -p:TreatWarningsAsErrors=true -p:CodeAnalysisTreatWarningsAsErrors=true
        if [[ -n "$smoke" ]]; then
            managed="$root/examples/$folder/$variant/bin/Release/net11.0/$rid"
            verify_outputs "$example-managed-smoke" "$kind" dotnet "$managed/$example.dll"
            hash_outputs "$example-managed-smoke" "$managed"
            continue
        fi

        trimmed="$evidence/$example-trimmed"
        run_logged "$log" dotnet publish "$project" --configuration Release \
            --runtime "$rid" --no-restore -p:BuildProjectReferences=false \
            --output "$trimmed" -p:PublishAot=false -p:PublishTrimmed=true \
            -p:TrimMode=link -p:SelfContained=true -p:EnableTrimAnalyzer=true \
            -p:TreatWarningsAsErrors=true -p:ILLinkTreatWarningsAsErrors=true
        verify_outputs "$example-trimmed" "$kind" dotnet "$trimmed/$example.dll"
        hash_outputs "$example-trimmed" "$trimmed"

        native="$evidence/$example-native"
        run_logged "$log" dotnet publish "$project" --configuration Release \
            --runtime "$rid" --no-restore -p:BuildProjectReferences=false \
            --output "$native" -p:PublishAot=true -p:PublishTrimmed=true \
            -p:EnableTrimAnalyzer=true -p:TreatWarningsAsErrors=true \
            -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true \
            "-p:CppCompilerAndLinker=$compiler"
        verify_outputs "$example-native" "$kind" "$native/$example"
        hash_outputs "$example-native" "$native"
    done
done

if [[ -n "$smoke" ]]; then
    echo "Managed-only conversion example smokes verified; trimmed/native publication not run." \
        | tee "$evidence/result.txt"
else
    echo "All four conversion examples verified after distinct trimmed and NativeAOT publication." \
        | tee "$evidence/result.txt"
fi
echo "Process-smoke evidence (not MSTest/Governance test-run acceptance): $evidence"
