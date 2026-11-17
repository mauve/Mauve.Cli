#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet tool restore --configfile NuGet.Config
dotnet tool run governance -- doctor --root .
dotnet tool run governance -- validate --root . --strict-planned
dotnet tool run governance -- generate --root . --output artifacts/traceability.json
dotnet restore Mauve.Cli.slnx --locked-mode
dotnet build Mauve.Cli.slnx -c Release --no-restore
dotnet format Mauve.Cli.slnx --verify-no-changes --no-restore
dotnet test Mauve.Cli.slnx -c Release --no-build
echo "Bootstrap baseline: product test projects currently contain zero tests."
bash scripts/verify-tooling.sh

rid="$(dotnet --info | awk '/^[[:space:]]*RID:/ { print $2; exit }')"
compiler="${CPP_COMPILER_AND_LINKER:-cc}"
command -v "$compiler" >/dev/null
echo "NativeAOT scaffold checks: RID=$rid, compiler=$compiler"
for example in Todo.Cli FileTool.Cli; do
    dotnet publish "examples/$example/$example.csproj" -c Release -r "$rid" \
        --no-restore -p:BuildProjectReferences=false \
        "-p:CppCompilerAndLinker=$compiler" -o "artifacts/native/$example"
    output="$("$root/artifacts/native/$example/$example")"
    if [[ "$output" != "$example scaffold: parser example not implemented." ]]; then
        echo "Unexpected native scaffold output: $output" >&2
        exit 1
    fi
done

dotnet tool run governance -- update --root . --check
dotnet tool run governance -- validate --root . --strict-planned
echo "Bootstrap verified. Native scaffold execution and tooling probes are not parser acceptance evidence."
