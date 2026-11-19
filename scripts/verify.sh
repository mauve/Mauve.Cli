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
bash scripts/verify-tooling.sh

bash scripts/verify-aot.sh

dotnet tool run governance -- update --root . --check
dotnet tool run governance -- validate --root . --strict-planned
echo "Repository verification complete."
