#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

usage() {
    cat <<'EOF'
Usage: bash scripts/fuzz.sh <completion|shell|csv|argv> [options]

Options:
  --seconds N    Campaign duration in seconds (default: 60; range: 1..86400).
  --bridge PATH  Existing trusted libfuzzer-dotnet executable (default: PATH lookup).
  --corpus DIR   Read-only seed directory (default: committed target corpus).
  --output DIR   New run directory beneath artifacts/fuzzing (relative to repo root).
  --help         Show this help.

Requires dotnet, jq, GNU-compatible timeout and a provisioned native bridge.
Clang/libFuzzer are bridge build prerequisites, not required by this runner
when the caller supplies an already built, statically linked bridge.
Restores the pinned nested local tool and locked project dependencies; never
downloads a native bridge or installs a global tool. Only the published copy of
Mauve.Cli.dll is instrumented; instrumenting the harness crashes at startup. Supplied/default seeds are
read-only inputs; generated corpus and crash artifacts remain in the run directory.
Relative corpus and output paths are resolved from the repository root.
Seed directories must not overlap build outputs or contain the run directory.
Each input is capped at 4096 bytes and five seconds. A wall-clock watchdog
terminates the bridge if its own campaign deadline does not finish.
EOF
}

fail() {
    echo "Fuzzing: $*" >&2
    exit 1
}

if (( $# == 0 )); then
    usage >&2
    exit 1
fi
if [[ "$1" == "--help" ]]; then
    usage
    exit 0
fi

target="$1"
shift
case "$target" in
    completion|shell|csv|argv) ;;
    *) fail "unknown target '$target'; expected completion, shell, csv or argv." ;;
esac

seconds=60
bridge=""
corpus=""
output=""
while (( $# > 0 )); do
    case "$1" in
        --seconds|--bridge|--corpus|--output)
            (( $# >= 2 )) || fail "missing value for $1."
            [[ -n "$2" ]] || fail "empty value for $1."
            case "$1" in
                --seconds) seconds="$2" ;;
                --bridge) bridge="$2" ;;
                --corpus) corpus="$2" ;;
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

[[ "$seconds" =~ ^[1-9][0-9]{0,4}$ ]] || fail "--seconds must be an integer from 1 to 86400."
(( seconds <= 86400 )) || fail "--seconds must not exceed 86400."

for dependency in dotnet jq timeout realpath sha256sum; do
    command -v "$dependency" >/dev/null || fail "required executable '$dependency' is unavailable."
done
timeout --signal=TERM --kill-after=1s 1s bash -c ':' \
    || fail "timeout must support --signal and --kill-after."

project="$root/fuzz/Mauve.Cli.Fuzzing/Mauve.Cli.Fuzzing.csproj"
manifest="$root/fuzz/.config/dotnet-tools.json"
seeds="${corpus:-$root/fuzz/Mauve.Cli.Fuzzing/corpus/$target}"
dictionary="$root/fuzz/Mauve.Cli.Fuzzing/dictionaries/$target.dict"
[[ -f "$project" ]] || fail "missing harness project: $project"
[[ -f "$root/fuzz/Mauve.Cli.Fuzzing/packages.lock.json" ]] || fail "missing harness package lock."
[[ -f "$manifest" ]] || fail "missing nested tool manifest: $manifest"
jq -e '
    .isRoot == true
    and .tools["sharpfuzz.commandline"].version == "2.3.0"
    and .tools["sharpfuzz.commandline"].commands == ["sharpfuzz"]
' "$manifest" >/dev/null || fail "nested manifest must pin SharpFuzz.CommandLine 2.3.0 and sharpfuzz."
[[ -d "$seeds" && ! -L "$seeds" ]] || fail "missing real seed directory for $target: $seeds"
seeds="$(realpath -e -- "$seeds")"
[[ -r "$seeds" && -x "$seeds" ]] || fail "seed directory must be readable and searchable: $seeds"
for build_project in fuzz/Mauve.Cli.Fuzzing src/Mauve.Cli src/Mauve.Cli.Generators; do
    for build_directory in bin obj; do
        writable="$(realpath -m -- "$root/$build_project/$build_directory")"
        [[ "$writable" != "$seeds" && "$writable" != "$seeds/"* \
            && "$seeds" != "$writable/"* ]] \
            || fail "read-only seed corpus must not overlap build outputs: $writable"
    done
done
invalid_seed="$(find "$seeds" -mindepth 1 \
    \( \( ! -type d ! -type f \) -o ! -readable \) -print -quit)"
[[ -z "$invalid_seed" ]] || fail "corpus must contain only readable regular files and directories: $invalid_seed"
seed="$(find "$seeds" -type f -print -quit)"
[[ -n "$seed" ]] || fail "seed directory has no regular seed files: $seeds"
[[ -f "$dictionary" && -r "$dictionary" ]] || fail "missing readable dictionary: $dictionary"

if [[ -z "$bridge" ]]; then
    bridge="$(command -v libfuzzer-dotnet || true)"
    [[ -n "$bridge" ]] || fail "libfuzzer-dotnet is unavailable; supply --bridge with a trusted local executable."
elif [[ "$bridge" != */* ]]; then
    bridge="$(command -v "$bridge" || true)"
fi
[[ -n "$bridge" && -f "$bridge" && -x "$bridge" ]] || fail "--bridge must identify an existing executable file."
bridge="$(realpath -e -- "$bridge")"

# Refuse reused directories and symlink escapes rather than cleaning caller data.
artifacts="$root/artifacts/fuzzing"
[[ "$(realpath -m -- "$artifacts")" == "$artifacts" ]] || fail "artifacts/fuzzing must not traverse symlinks."
output="${output:-$artifacts/$target/$(date -u +%Y%m%dT%H%M%SZ)-$$}"
output="$(realpath -m -- "$output")"
[[ "$output" == "$artifacts/"* ]] || fail "--output must be beneath $artifacts."
[[ "$output" != "$seeds" && "$output" != "$seeds/"* ]] \
    || fail "--output must not write into the read-only seed corpus: $seeds"
[[ ! -e "$output" && ! -L "$output" ]] || fail "run directory already exists: $output"
mkdir -p -- "$(dirname "$output")"
mkdir -- "$output"
publish="$output/publish"
generated="$output/corpus"
findings="$output/findings"
mkdir -- "$publish" "$generated" "$findings"

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
    echo "Target: $target"
    echo "Duration seconds: $seconds"
    echo "Maximum input bytes: 4096"
    echo "Input timeout seconds: 5"
    echo "Bridge: $bridge"
    echo "Read-only seed corpus: $seeds"
    echo "Native dependencies beyond the bridge are verified only by actual execution."
    echo "Tool manifest, bridge, dictionary and seed SHA-256:"
    sha256sum "$manifest" "$bridge" "$dictionary"
    find "$seeds" -type f -exec sha256sum -- {} +
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
product="$publish/Mauve.Cli.dll"
for assembly in "$product" "$harness"; do
    [[ -f "$assembly" && ! -L "$assembly" ]] || fail "missing published assembly copy: $assembly"
done
[[ -f "$publish/Mauve.Cli.Fuzzing.runtimeconfig.json" ]] || fail "missing published runtime configuration."
[[ -f "$publish/Mauve.Cli.Fuzzing.deps.json" ]] || fail "missing published dependency configuration."
(
    # isRoot in the nested manifest prevents falling back to root/global tools.
    cd "$root/fuzz"
    run_logged dotnet tool run sharpfuzz -- "$product"
)
sha256sum "$product" >"$output/instrumented-assemblies.sha256"

export MAUVE_FUZZ_TARGET="$target"
echo "Starting $target fuzzing; generated inputs and findings: $output"
run_logged timeout --signal=TERM --kill-after=10s "$(( seconds + 30 ))s" \
    "$bridge" --target_path=dotnet "--target_arg=$harness" \
    "-max_total_time=$seconds" -max_len=4096 -timeout=5 \
    "-dict=$dictionary" "-artifact_prefix=$findings/" \
    "$generated" "$seeds"
echo "Fuzz campaign completed: $output"
