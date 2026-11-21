#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

rid="${1:-linux-x64}"
host_rid="$(dotnet --info | awk '/^[[:space:]]*RID:/ { print $2; exit }')"
machine="$(uname -m)"
compiler="${CPP_COMPILER_AND_LINKER:-clang}"
evidence="$root/artifacts/aot-verification/$rid/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$evidence"

if [[ "$rid" != "linux-x64" || "$host_rid" != "$rid" || "$machine" != "x86_64" ]]; then
    echo "This script requires a native linux-x64 host; requested=$rid host=$host_rid machine=$machine" >&2
    exit 1
fi

command -v "$compiler" >/dev/null
command -v jq >/dev/null
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
        examples/FileTool.Cli/packages.lock.json \
        tests/Mauve.Cli.Aot.Tests/packages.lock.json
    echo "Acceptance source SHA-256:"
    sha256sum \
        examples/Todo.Cli/Program.cs \
        examples/Todo.Cli/Todo.Cli.csproj \
        examples/FileTool.Cli/Program.cs \
        examples/FileTool.Cli/FileTool.Cli.csproj \
        src/Mauve.Cli/Invocation/CliInvoker.cs \
        src/Mauve.Cli/Invocation/PredefinedDirectives.cs \
        src/Mauve.Cli/Completion/SuggestCommandLine.cs \
        src/Mauve.Cli/Invocation/CliHostOptions.cs \
        src/Mauve.Cli/Invocation/IGeneratedCliCommand.cs \
        src/Mauve.Cli.Generators/CliModelGenerator.cs \
        tests/Mauve.Cli.Tests/Invocation/CliInvokerTests.cs \
        tests/Mauve.Cli.Generators.Tests/CliModelGeneratorTests.cs \
        tests/Mauve.Cli.Aot.Tests/AotScenario.cs \
        tests/Mauve.Cli.Aot.Tests/AotDeclarations.cs \
        tests/Mauve.Cli.Aot.Tests/AotAcceptanceTests.cs \
        tests/Mauve.Cli.Aot.Tests/Mauve.Cli.Aot.Tests.csproj \
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

verify_example_directives() {
    local log="$1"
    local example="$2"
    shift 2
    local output
    local payload
    local expected
    if [[ "$example" == "Todo.Cli" ]]; then
        payload="todo ad"
        expected="add"
    else
        payload="tool c"
        expected="copy"
    fi

    {
        printf '$'
        printf ' %q' "$@" "[suggest:${#payload}]" "$payload"
        printf '\n'
    } | tee -a "$log"
    output="$("$@" "[suggest:${#payload}]" "$payload")"
    printf 'Directive completion output: %s\n' "$output" | tee -a "$log"
    if [[ "$output" != "$expected" ]]; then
        echo "$example returned unexpected directive completion." >&2
        exit 1
    fi

    {
        printf '$'
        printf ' %q' "$@" --help
        printf '\n'
    } | tee -a "$log"
    output="$("$@" --help)"
    printf '%s\n' "$output" >>"$log"
    if [[ "$output" != *"[debug]"* || "$output" != *"[suggest"* ]]; then
        echo "$example did not register both predefined directives." >&2
        exit 1
    fi
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
    if [[ "$example" == "Todo.Cli" ]]; then
        run_logged "$evidence/$example.log" dotnet "$trimmed/$example.dll" new "AOT check"
        grep -Fqx "Added: AOT check" "$evidence/$example.log"
    else
        run_logged "$evidence/$example.log" dotnet "$trimmed/$example.dll" --verify-aot
    fi
    verify_example_directives "$evidence/$example.log" "$example" dotnet "$trimmed/$example.dll"
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
    if [[ "$example" == "Todo.Cli" ]]; then
        run_logged "$evidence/$example.log" "$native/$example" new "AOT check"
        grep -Fqx "Added: AOT check" "$evidence/$example.log"
    else
        run_logged "$evidence/$example.log" "$native/$example" --verify-aot
    fi
    verify_example_directives "$evidence/$example.log" "$example" "$native/$example"
    if find "$native" -iname '*Mauve.Cli.Generators*' -print -quit | grep -q .; then
        echo "$example native output contains the build-time generator." >&2
        exit 1
    fi
    sha256sum "$native/$example" | tee -a "$evidence/$example.log"
done

test_project="tests/Mauve.Cli.Aot.Tests/Mauve.Cli.Aot.Tests.csproj"
test_name="Mauve.Cli.Aot.Tests"
run_logged "$evidence/$test_name.log" dotnet restore "$test_project" --locked-mode
run_logged "$evidence/$test_name.log" dotnet build "$test_project" --configuration Release --no-restore

managed_results="$evidence/$test_name-managed-results"
mkdir -p "$managed_results"
run_logged "$evidence/$test_name.log" \
    dotnet test "$test_project" --configuration Release --no-restore --no-build \
    --report-trx --results-directory "$managed_results"

native="$evidence/$test_name-native"
run_logged "$evidence/$test_name.log" \
    dotnet publish "$test_project" --configuration Release --runtime "$rid" --no-restore \
    -p:BuildProjectReferences=false \
    --output "$native" \
    -p:PublishAot=true \
    -p:PublishTrimmed=true \
    -p:EnableTrimAnalyzer=true \
    -p:ILLinkTreatWarningsAsErrors=true \
    -p:IlcTreatWarningsAsErrors=true \
    "-p:CppCompilerAndLinker=$compiler"

native_results="$evidence/$test_name-native-results"
mkdir -p "$native_results"
run_logged "$evidence/$test_name.log" \
    "$native/$test_name" --report-trx --results-directory "$native_results"

for mode in managed native; do
    results_variable="${mode}_results"
    results_directory="${!results_variable}"
    mapfile -t reports < <(find "$results_directory" -maxdepth 1 -type f -name '*.trx')
    if (( ${#reports[@]} != 1 )); then
        echo "Expected exactly one $mode AOT test TRX report." >&2
        exit 1
    fi

    dotnet tool run governance -- test-results --trx "${reports[0]}" \
        --output "$evidence/$test_name-$mode-evidence.json"
    jq -e '
        (.Tests | length == 2)
        and (.Tests | all(.Status == "Passed" and .Passed == 1))
        and (.Features | any(.Id == "FEAT-13" and .Status == "Passed" and .Passed == 1))
        and (.Features | any(.Id == "FEAT-15" and .Status == "Passed" and .Passed == 1))
        and ([.Requirements[]
            | select(.Id | startswith("FEAT-13-"))
            | select(.Status == "Passed" and .Passed == 1)] | length == 4)
        and ([.Requirements[]
            | select(.Id | startswith("FEAT-15-"))
            | select(.Status == "Passed" and .Passed == 1)] | length == 3)
    ' "$evidence/$test_name-$mode-evidence.json" >/dev/null
done

sha256sum "$native/$test_name" | tee -a "$evidence/$test_name.log"

echo "Trimmed examples and managed/NativeAOT test evidence: $evidence"
