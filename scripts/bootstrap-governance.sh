#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
checkout="$(realpath "${1:-"$root/../Mauve.Governance"}")"
version="0.14.0"
feed="$root/artifacts/governance-feed"
tool_dir="$root/artifacts/governance-tool"
export NUGET_PACKAGES="$root/artifacts/nuget"

if [[ ! -f "$checkout/src/Mauve.Governance.Tool/Mauve.Governance.Tool.csproj" ]]; then
    echo "Expected a Mauve.Governance checkout at $checkout" >&2
    exit 1
fi

actual_version="$(
    cd "$checkout"
    dotnet msbuild src/Mauve.Governance.Tool/Mauve.Governance.Tool.csproj \
        -getProperty:Version -nologo
)"
if [[ "$actual_version" != "$version" ]]; then
    echo "Expected Governance $version, found $actual_version; review the pin before upgrading." >&2
    exit 1
fi

mkdir -p "$feed" "$root/artifacts"
{
    printf 'Version: %s\n' "$version"
    if git -C "$checkout" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
        printf 'Source commit: '
        git -C "$checkout" rev-parse HEAD
        printf 'Source status (local changes are part of the trusted input):\n'
        git -C "$checkout" --no-pager status --short
    else
        printf 'Source has no Git revision; retain the trusted snapshot separately.\n'
    fi
} >"$root/artifacts/governance-source.txt"
projects=(
    TestResults Traceability Requirements Testing Testing.MSTest
    Testing.Analyzers Logging.Analyzers Testing.TrxLogger Tool
)
staging="$(mktemp -d "$root/artifacts/governance-pack.XXXXXX")"
cleanup() {
    for project in "${projects[@]}"; do
        rm -f -- "$staging/Mauve.Governance.$project.$version.nupkg"
    done
    rmdir "$staging"
}
trap cleanup EXIT

for project in "${projects[@]}"; do
    package="$staging/Mauve.Governance.$project.$version.nupkg"
    (
        cd "$checkout"
        dotnet pack "src/Mauve.Governance.$project" -c Release -o "$staging" \
            -p:RestoreLockedMode=true -p:DeterministicTimestamp=2000-01-01T00:00:00Z
    )
    package_id="mauve.governance.${project,,}"
    cached="$NUGET_PACKAGES/$package_id/$version/$package_id.$version.nupkg"
    if [[ -f "$cached" ]] && ! cmp -s "$package" "$cached"; then
        echo "$package_id $version differs from this checkout's cached bytes." >&2
        echo "Use the matching trusted source, or review a package/lock update in a fresh isolated checkout." >&2
        exit 1
    fi
done
for project in "${projects[@]}"; do
    cp "$staging/Mauve.Governance.$project.$version.nupkg" "$feed/"
done
sha256sum "$feed"/*.nupkg >"$root/artifacts/governance-packages.sha256"

if [[ ! -x "$tool_dir/governance" ]]; then
    dotnet tool install Mauve.Governance.Tool --version "$version" \
        --tool-path "$tool_dir" --configfile "$root/NuGet.Config"
fi

cd "$root"
if [[ ! -f .mauve-governance/manifest.json ]]; then
    status=0
    "$tool_dir/governance" init --root "$root" --profile core-requirements \
        --bundle-version "$version" --check || status=$?
    if [[ "$status" != 0 && "$status" != 1 ]]; then
        echo "Governance enrollment preview failed with exit code $status." >&2
        exit "$status"
    fi
    "$tool_dir/governance" init --root "$root" --profile core-requirements \
        --bundle-version "$version"
fi

dotnet tool restore --configfile NuGet.Config
dotnet tool run governance -- validate --root . --strict-planned
