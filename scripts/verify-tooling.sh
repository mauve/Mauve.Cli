#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
command -v jq >/dev/null

# Governance ignores obj, but not artifacts; never scan synthetic tests as product evidence.
probe="$root/artifacts/obj/governance-probe"
mkdir -p "$probe"
cp scripts/probes/Probe.csproj.in "$probe/Probe.csproj"
cp scripts/probes/Directory.Build.props.in "$probe/Directory.Build.props"
cp scripts/probes/RunnerTests.cs.in "$probe/RunnerTests.cs"
cp tests/.editorconfig "$probe/.editorconfig"
trap 'rm -f -- "$probe/MissingMetadata.cs" "$probe/StyleViolation.cs"' EXIT
rm -f -- "$probe/MissingMetadata.cs" "$probe/StyleViolation.cs"
results="$(mktemp -d "$probe/results.XXXXXX")"

dotnet restore "$probe/Probe.csproj" --force-evaluate -p:RestoreLockedMode=false
dotnet test "$probe/Probe.csproj" -c Release --no-restore --logger governance-trx \
    --results-directory "$results"

mapfile -t reports < <(find "$results" -maxdepth 1 -type f -name 'governance-*.trx')
if (( ${#reports[@]} != 1 )); then
    echo "Expected exactly one fresh Governance TRX report." >&2
    exit 1
fi
report="${reports[0]}"
dotnet tool run governance -- test-results --trx "$report" \
    --output "$probe/test-evidence.json"
jq -e '
    (.Runs | length == 1)
    and (.Tests | length == 1)
    and (.Features | any(.Id == "FEAT-Infrastructure" and .Status == "Passed" and .Passed == 1))
    and (.Requirements | any(.Id == "FEAT-Infrastructure-001" and .Status == "Passed" and .Passed == 1))
' "$probe/test-evidence.json" >/dev/null

cp scripts/probes/MissingMetadata.cs.in "$probe/MissingMetadata.cs"
if dotnet build "$probe/Probe.csproj" -c Release --no-restore >"$probe/metadata-negative.log" 2>&1; then
    echo "Expected the missing-metadata analyzer probe to fail." >&2
    exit 1
fi
if ! grep -q 'error MGTEST001' "$probe/metadata-negative.log"; then
    cat "$probe/metadata-negative.log" >&2
    echo "The negative probe failed without the expected MGTEST001 error." >&2
    exit 1
fi
rm -- "$probe/MissingMetadata.cs"

cp scripts/probes/StyleViolation.cs.in "$probe/StyleViolation.cs"
if dotnet build "$probe/Probe.csproj" -c Release --no-restore >"$probe/style-negative.log" 2>&1; then
    echo "Expected the block-namespace style probe to fail." >&2
    exit 1
fi
if ! grep -q 'error IDE0161' "$probe/style-negative.log"; then
    cat "$probe/style-negative.log" >&2
    echo "The negative probe failed without the expected IDE0161 error." >&2
    exit 1
fi
rm -- "$probe/StyleViolation.cs"

echo "Infrastructure only: one real runner/TRX observation and two expected compiler rejections."
