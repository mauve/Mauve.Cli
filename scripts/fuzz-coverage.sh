#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

usage() {
    cat <<'EOF'
Usage: bash scripts/fuzz-coverage.sh [options]

Replays the committed fuzz corpus (plus optional extra corpora) through the
harness under dotnet-coverage and reports line/branch coverage of Mauve.Cli.

Options:
  --target NAME        completion, shell, csv or argv; repeatable (default: all).
  --extra-corpus T=DIR Additional read-only corpus for target T; repeatable,
                       for example a generated campaign corpus.
  --output DIR         New run directory beneath artifacts (default:
                       artifacts/coverage/fuzz-<UTC timestamp>-<pid>).
  --help               Show this help.

Requires dotnet and jq. Restores the pinned nested tools (dotnet-coverage and
ReportGenerator) and locked project dependencies; never installs a global tool
and never modifies a corpus. Coverage covers the whole Mauve.Cli assembly, so
percentages measure what the replayed inputs execute, not defect absence.
Output: <run>/coverage/*.cobertura.xml, <run>/report/<name>/Summary.txt and
index.html, plus environment.txt and run.log recording versions and corpus hashes.
EOF
}

fail() {
    echo "Fuzz coverage: $*" >&2
    exit 1
}

valid_target() {
    case "$1" in
        completion|shell|csv|argv) return 0 ;;
        *) return 1 ;;
    esac
}

targets=()
extras=()
output=""
while (( $# > 0 )); do
    case "$1" in
        --target|--extra-corpus|--output)
            (( $# >= 2 )) || fail "missing value for $1."
            [[ -n "$2" ]] || fail "empty value for $1."
            case "$1" in
                --target)
                    valid_target "$2" || fail "unknown target '$2'; expected completion, shell, csv or argv."
                    targets+=("$2")
                    ;;
                --extra-corpus)
                    [[ "$2" == *=* ]] || fail "--extra-corpus expects TARGET=DIR."
                    valid_target "${2%%=*}" || fail "unknown target '${2%%=*}' in --extra-corpus."
                    extras+=("$2")
                    ;;
                --output) output="$2" ;;
            esac
            shift 2
            ;;
        --help)
            usage
            exit 0
            ;;
        *) fail "unknown option '$1'." ;;
    esac
done
(( ${#targets[@]} > 0 )) || targets=(completion shell csv argv)

for dependency in dotnet jq realpath sha256sum; do
    command -v "$dependency" >/dev/null || fail "required executable '$dependency' is unavailable."
done

project="$root/fuzz/Mauve.Cli.Fuzzing/Mauve.Cli.Fuzzing.csproj"
manifest="$root/fuzz/.config/dotnet-tools.json"
[[ -f "$project" ]] || fail "missing harness project: $project"
[[ -f "$root/fuzz/Mauve.Cli.Fuzzing/packages.lock.json" ]] || fail "missing harness package lock."
[[ -f "$manifest" ]] || fail "missing nested tool manifest: $manifest"
jq -e '
    .isRoot == true
    and .tools["dotnet-coverage"].version == "18.12.0"
    and .tools["dotnet-reportgenerator-globaltool"].version == "5.6.1"
' "$manifest" >/dev/null \
    || fail "nested manifest must pin dotnet-coverage 18.12.0 and dotnet-reportgenerator-globaltool 5.6.1."

# dotnet-coverage needs to locate the runtime that hosts the harness.
if [[ -z "${DOTNET_ROOT:-}" ]]; then
    DOTNET_ROOT="$(dirname "$(realpath -e -- "$(command -v dotnet)")")"
fi
[[ -x "$DOTNET_ROOT/dotnet" ]] || fail "DOTNET_ROOT does not contain a dotnet executable: $DOTNET_ROOT"
export DOTNET_ROOT

corpus_dir_for() {
    echo "$root/fuzz/Mauve.Cli.Fuzzing/corpus/$1"
}

# Inputs are read-only; refuse anything that is not a plain readable file tree.
validate_corpus_dir() {
    local directory="$1" invalid
    [[ -d "$directory" && ! -L "$directory" ]] || fail "missing real corpus directory: $directory"
    [[ -r "$directory" && -x "$directory" ]] || fail "corpus directory must be readable and searchable: $directory"
    invalid="$(find "$directory" -mindepth 1 \
        \( \( ! -type d ! -type f \) -o ! -readable \) -print -quit)"
    [[ -z "$invalid" ]] || fail "corpus must contain only readable regular files and directories: $invalid"
}

artifacts="$root/artifacts"
[[ "$(realpath -m -- "$artifacts")" == "$artifacts" ]] || fail "artifacts must not traverse symlinks."
output="${output:-$artifacts/coverage/fuzz-$(date -u +%Y%m%dT%H%M%SZ)-$$}"
output="$(realpath -m -- "$output")"
[[ "$output" == "$artifacts/"* ]] || fail "--output must be beneath $artifacts."
[[ ! -e "$output" && ! -L "$output" ]] || fail "run directory already exists: $output"

for target in "${targets[@]}"; do
    validate_corpus_dir "$(corpus_dir_for "$target")"
done
extra_dirs=()
for extra in "${extras[@]}"; do
    directory="$(realpath -e -- "${extra#*=}")" || fail "extra corpus does not exist: ${extra#*=}"
    validate_corpus_dir "$directory"
    [[ "$directory" != "$output" && "$directory" != "$output/"* ]] \
        || fail "extra corpus must not be inside the run directory: $directory"
    extra_dirs+=("${extra%%=*}=$directory")
done

mkdir -p -- "$(dirname "$output")"
mkdir -- "$output"
publish="$output/publish"
coverage="$output/coverage"
reports="$output/report"
mkdir -- "$publish" "$coverage" "$reports"

run_logged() {
    {
        printf '$'
        printf ' %q' "$@"
        printf '\n'
    } | tee -a "$output/run.log"
    "$@" 2>&1 | tee -a "$output/run.log"
}

{
    echo "UTC: $(date -u +%FT%TZ)"
    echo "Commit: $(git rev-parse HEAD)"
    echo "Uncommitted changes: $(git status --porcelain | wc -l) path(s)"
    echo "Targets: ${targets[*]}"
    echo "DOTNET_ROOT: $DOTNET_ROOT"
    echo "Tool manifest SHA-256:"
    sha256sum "$manifest"
    jq -r '.tools | to_entries[] | "\(.key) \(.value.version)"' "$manifest"
    echo "Corpus SHA-256 (committed, then extra):"
    for target in "${targets[@]}"; do
        find "$(corpus_dir_for "$target")" -type f -exec sha256sum -- {} + | sort -k2
    done
    for extra in "${extra_dirs[@]}"; do
        find "${extra#*=}" -type f -exec sha256sum -- {} + | sort -k2
    done
    echo "SDK:"
    dotnet --info
} >"$output/environment.txt" 2>&1

(
    cd "$root/fuzz"
    run_logged dotnet tool restore --tool-manifest "$manifest"
)
run_logged dotnet restore "$project" --locked-mode \
    -p:PublishAot=false -p:PublishTrimmed=false -p:PublishReadyToRun=false
run_logged dotnet publish "$project" --configuration Release --no-restore \
    --self-contained false --output "$publish" \
    -p:PublishAot=false -p:PublishTrimmed=false -p:PublishReadyToRun=false \
    -p:UseAppHost=false

harness="$publish/Mauve.Cli.Fuzzing.dll"
for assembly in "$harness" "$publish/Mauve.Cli.dll" "$publish/Mauve.Cli.pdb"; do
    [[ -f "$assembly" && ! -L "$assembly" ]] || fail "missing published file (coverage needs symbols): $assembly"
done

reports_for=()
for target in "${targets[@]}"; do
    inputs=("$(corpus_dir_for "$target")")
    for extra in "${extra_dirs[@]}"; do
        if [[ "${extra%%=*}" == "$target" ]]; then
            inputs+=("${extra#*=}")
        fi
    done
    count="$(find "${inputs[@]}" -type f | wc -l)"
    echo "Replaying $count $target input(s) under coverage." | tee -a "$output/run.log"
    # The tool runs from fuzz/ so the nested manifest (isRoot) is the only one consulted.
    (
        cd "$root/fuzz"
        run_logged dotnet tool run dotnet-coverage -- collect \
            --output-format cobertura --output "$coverage/$target.cobertura.xml" \
            -- dotnet "$harness" "$target" "${inputs[@]}"
    )
    [[ -s "$coverage/$target.cobertura.xml" ]] || fail "no coverage produced for $target."
    reports_for+=("$target")
done

merged="$coverage/merged.cobertura.xml"
(
    cd "$root/fuzz"
    run_logged dotnet tool run dotnet-coverage -- merge \
        --output-format cobertura --output "$merged" "$coverage"/*.cobertura.xml
)
[[ -s "$merged" ]] || fail "merged coverage was not produced."

report() {
    local name="$1" input="$2"
    (
        cd "$root/fuzz"
        run_logged dotnet tool run reportgenerator -- \
            "-reports:$input" "-targetdir:$reports/$name" \
            "-reporttypes:Html;TextSummary" "-assemblyfilters:+Mauve.Cli" \
            -verbosity:Warning
    )
}
for target in "${reports_for[@]}"; do
    report "$target" "$coverage/$target.cobertura.xml"
done
report merged "$merged"

echo
echo "Merged coverage of the Mauve.Cli assembly (all replayed targets):"
cat "$reports/merged/Summary.txt"
echo
echo "Fuzz coverage run completed: $output"
