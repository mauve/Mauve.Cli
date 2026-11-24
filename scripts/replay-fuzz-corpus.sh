#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

usage() {
    cat <<'EOF'
Usage: bash scripts/replay-fuzz-corpus.sh <completion|shell|csv|argv|all>

Replays every file in the selected committed corpus exactly once, in
stable path order, using one harness process per target. This script never builds the harness or modifies corpora.
Build first with: dotnet build Mauve.Cli.slnx -c Release
EOF
}

fail() {
    echo "Fuzz corpus replay: $*" >&2
    exit 1
}

if (( $# != 1 )); then
    usage >&2
    exit 1
fi

case "$1" in
    completion|shell|csv|argv)
        targets=("$1")
        ;;
    all)
        targets=(completion shell csv argv)
        ;;
    --help)
        usage
        exit 0
        ;;
    *)
        fail "invalid target '$1'; expected completion, shell, csv, argv or all."
        ;;
esac

command -v dotnet >/dev/null || fail "required executable 'dotnet' is unavailable."
harness="$root/fuzz/Mauve.Cli.Fuzzing/bin/Release/net11.0/Mauve.Cli.Fuzzing.dll"
[[ -f "$harness" && -r "$harness" ]] \
    || fail "Release harness is unavailable; build it first with dotnet build Mauve.Cli.slnx -c Release."

declare -A seed_counts
for target in "${targets[@]}"; do
    corpus="$root/fuzz/Mauve.Cli.Fuzzing/corpus/$target"
    [[ -d "$corpus" && ! -L "$corpus" ]] || fail "missing corpus directory for $target: $corpus"
    mapfile -d '' -t seeds < <(find "$corpus" -type f -print0 | LC_ALL=C sort -z)
    (( ${#seeds[@]} > 0 )) || fail "no seed files found for $target in $corpus."
    seed_counts["$target"]="${#seeds[@]}"
done

for target in "${targets[@]}"; do
    corpus="$root/fuzz/Mauve.Cli.Fuzzing/corpus/$target"
    mapfile -d '' -t seeds < <(find "$corpus" -type f -print0 | LC_ALL=C sort -z)
    [[ "${#seeds[@]}" -eq "${seed_counts[$target]}" ]] || fail "seed set changed during replay for $target."
    printf 'Replaying %d %s corpus seed(s).\n' "${#seeds[@]}" "$target"
    for seed in "${seeds[@]}"; do
        [[ -f "$seed" && ! -L "$seed" ]] || fail "seed is no longer a regular file: $seed"
    done
    # One process per target; the harness walks the directory in ordinal order
    # and reports the failing input path if an invariant or unexpected exception escapes.
    dotnet "$harness" "$target" "$corpus"
done

echo "Fuzz corpus replay completed."
