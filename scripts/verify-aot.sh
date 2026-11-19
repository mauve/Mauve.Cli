#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

rid="${1:-linux-x64}"
host_rid="$(dotnet --info | awk '/^[[:space:]]*RID:/ { print $2; exit }')"
machine="$(uname -m)"
compiler="${CPP_COMPILER_AND_LINKER:-clang}"
evidence="artifacts/aot-verification/$rid/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$evidence"

if [[ "$rid" != "linux-x64" || "$host_rid" != "$rid" || "$machine" != "x86_64" ]]; then
    echo "This script requires a native linux-x64 host; requested=$rid host=$host_rid machine=$machine" >&2
    exit 1
fi

command -v "$compiler" >/dev/null
{
    echo "UTC: $(date -u +%FT%TZ)"
    echo "RID: $rid"
    echo "Host RID: $host_rid"
    echo "Machine: $machine"
    echo "Compiler: $compiler"
    echo "Commit: $(git rev-parse HEAD)"
    echo "Working tree:"
    git status --short
    echo "Tracked diff SHA-256:"
    git diff --binary | sha256sum
    echo "SDK:"
    dotnet --info
    echo "Compiler version:"
    "$compiler" --version
    echo "Lockfile SHA-256:"
    sha256sum \
        src/Mauve.Cli/packages.lock.json \
        src/Mauve.Cli.Generators/packages.lock.json \
        examples/Todo.Cli/packages.lock.json \
        examples/FileTool.Cli/packages.lock.json
    echo "Acceptance source SHA-256:"
    sha256sum \
        examples/Aot.Scenarios/AotScenario.cs \
        examples/Todo.Cli/AotDeclarations.cs \
        examples/Todo.Cli/Program.cs \
        examples/Todo.Cli/Todo.Cli.csproj \
        examples/FileTool.Cli/Program.cs \
        examples/FileTool.Cli/FileTool.Cli.csproj \
        src/Mauve.Cli.Generators/CliModelGenerator.cs \
        tests/Mauve.Cli.Tests/Aot/AotAcceptanceTests.cs \
        tests/Mauve.Cli.Tests/Mauve.Cli.Tests.csproj \
        scripts/verify-aot.sh \
        docs/Architecture/ADR-013-NativeAot-Support-Matrix.md \
        docs/Requirements/FEAT-13-NativeAot-And-Trimming.md
} >"$evidence/environment.txt" 2>&1

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

for example in Todo.Cli FileTool.Cli; do
    project="examples/$example/$example.csproj"
    run_logged "$evidence/$example.log" dotnet restore "$project" --locked-mode
    run_logged "$evidence/$example.log" dotnet build "$project" --configuration Release --no-restore

    trimmed="$evidence/$example-trimmed"
    run_logged "$evidence/$example.log" \
        dotnet publish "$project" --configuration Release --runtime "$rid" --no-restore \
        -p:BuildProjectReferences=false \
        --output "$trimmed" \
        -p:PublishAot=false \
        -p:PublishTrimmed=true \
        -p:TrimMode=link \
        -p:SelfContained=true \
        -p:EnableTrimAnalyzer=true \
        -p:ILLinkTreatWarningsAsErrors=true
    run_logged "$evidence/$example.log" dotnet "$trimmed/$example.dll" --verify-aot
    if find "$trimmed" -iname '*Mauve.Cli.Generators*' -print -quit | grep -q .; then
        echo "$example trimmed output contains the build-time generator." >&2
        exit 1
    fi
    sha256sum "$trimmed/$example.dll" | tee -a "$evidence/$example.log"

    native="$evidence/$example-native"
    run_logged "$evidence/$example.log" \
        dotnet publish "$project" --configuration Release --runtime "$rid" --no-restore \
        -p:BuildProjectReferences=false \
        --output "$native" \
        -p:PublishAot=true \
        -p:PublishTrimmed=true \
        -p:EnableTrimAnalyzer=true \
        -p:ILLinkTreatWarningsAsErrors=true \
        -p:IlcTreatWarningsAsErrors=true \
        "-p:CppCompilerAndLinker=$compiler"
    run_logged "$evidence/$example.log" "$native/$example" --verify-aot
    if find "$native" -iname '*Mauve.Cli.Generators*' -print -quit | grep -q .; then
        echo "$example native output contains the build-time generator." >&2
        exit 1
    fi
    sha256sum "$native/$example" | tee -a "$evidence/$example.log"
done

echo "Trimmed managed and NativeAOT publish/run evidence: $evidence"
