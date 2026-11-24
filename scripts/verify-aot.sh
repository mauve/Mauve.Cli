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
        examples/02-Commands/Declarative/packages.lock.json \
        examples/02-Commands/Manual/packages.lock.json \
        tests/Mauve.Cli.Aot.Tests/packages.lock.json
    echo "Acceptance source SHA-256:"
    sha256sum \
        examples/02-Commands/Declarative/Program.cs \
        examples/02-Commands/Declarative/Commands.Declarative.csproj \
        examples/02-Commands/Manual/Program.cs \
        examples/02-Commands/Manual/Commands.Manual.csproj \
        examples/05-Validation/Declarative/Program.cs \
        examples/05-Validation/Manual/Program.cs \
        src/Mauve.Cli/Invocation/CliInvoker.cs \
        src/Mauve.Cli/Invocation/PredefinedDirectives.cs \
        src/Mauve.Cli/Completion/CompletionCommandLine.cs \
        src/Mauve.Cli/Completion/ShellCompletion.cs \
        src/Mauve.Cli/Invocation/CliHostOptions.cs \
        src/Mauve.Cli/Invocation/CliHostOptionsBuilder.cs \
        src/Mauve.Cli/Invocation/CliExceptionResult.cs \
        src/Mauve.Cli/Invocation/CliExitCodeException.cs \
        src/Mauve.Cli/Invocation/CliExceptionRenderingContext.cs \
        src/Mauve.Cli/Invocation/ExceptionMappingRegistry.cs \
        src/Mauve.Cli/Invocation/IGeneratedCliCommand.cs \
        src/Mauve.Cli/Parsing/CliParser.cs \
        src/Mauve.Cli/Parsing/ValueOrigin.cs \
        src/Mauve.Cli/Conversion/ConversionContext.cs \
        src/Mauve.Cli/Definitions/EnvironmentVariableBinding.cs \
        src/Mauve.Cli/Declarations/CliAttributes.cs \
        src/Mauve.Cli/Declarations/CliValidationAttributes.cs \
        src/Mauve.Cli/Validation/CliValidationException.cs \
        src/Mauve.Cli/Validation/CliValidators.cs \
        src/Mauve.Cli/Validation/ICliValidator.cs \
        src/Mauve.Cli/Validation/ValueValidationOutcome.cs \
        src/Mauve.Cli/Parsing/ParsedValue.cs \
        src/Mauve.Cli/Conversion/ICliValueConverter.cs \
        src/Mauve.Cli/Conversion/ValueConverterRegistration.cs \
        src/Mauve.Cli/Conversion/UnionConverterBuilder.cs \
        src/Mauve.Cli/Conversion/ConversionResolver.cs \
        src/Mauve.Cli/Conversion/ConverterRegistry.cs \
        src/Mauve.Cli/Conversion/ValueConverterEngine.cs \
        src/Mauve.Cli/Definitions/CommandBuilder.cs \
        src/Mauve.Cli/Definitions/CommandModel.cs \
        src/Mauve.Cli/Definitions/OptionDefinition.cs \
        src/Mauve.Cli/Definitions/PositionalDefinition.cs \
        src/Mauve.Cli/Definitions/DirectiveValueDefinition.cs \
        src/Mauve.Cli/Completion/CompletionService.cs \
        src/Mauve.Cli.Generators/CliModelGenerator.cs \
        tests/Mauve.Cli.Tests/Invocation/CliInvokerTests.cs \
        tests/Mauve.Cli.Tests/Invocation/ExceptionHostingTests.cs \
        tests/Mauve.Cli.Generators.Tests/CliModelGeneratorTests.cs \
        tests/Mauve.Cli.Aot.Tests/AotScenario.cs \
        tests/Mauve.Cli.Aot.Tests/AotConversionScenario.cs \
        tests/Mauve.Cli.Aot.Tests/AotDeclarations.cs \
        tests/Mauve.Cli.Aot.Tests/AotAcceptanceTests.cs \
        tests/Mauve.Cli.Aot.Tests/Mauve.Cli.Aot.Tests.csproj \
        docs/Requirements/FEAT-16-Environment-Option-Binding.md \
        docs/Architecture/ADR-016-Environment-Option-Binding.md \
        scripts/verify-aot.sh \
        scripts/verify-conversion-examples.sh \
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
    payload="commands gr"
    expected="greet"

    {
        printf '$'
        printf ' %q' "$@" '[completion:query-v1]' -- bash "$payload" "$payload"
        printf '\n'
    } | tee -a "$log"
    output="$("$@" "[completion:query-v1]" -- bash "$payload" "$payload")"
    printf 'Shell completion output: %s\n' "$output" | tee -a "$log"
    if [[ "$output" != "x$(printf '%s' "$expected" | base64)" ]]; then
        echo "$example returned unexpected shell completion." >&2
        exit 1
    fi
    "$@" '[completion:bash]' | bash -n
    echo "Bash registration syntax verified." | tee -a "$log"

    {
        printf '$'
        printf ' %q' "$@" --help
        printf '\n'
    } | tee -a "$log"
    output="$("$@" --help)"
    printf '%s\n' "$output" >>"$log"
    if [[ "$output" != *"[debug]"* || "$output" != *"[completion"* ]]; then
        echo "$example did not register both predefined directives." >&2
        exit 1
    fi
}

verify_example_commands() {
    local log="$1"
    shift
    local output
    local status
    {
        printf '$'
        printf ' %q' "$@" hi "AOT check"
        printf '\n'
    } | tee -a "$log"
    output="$("$@" hi "AOT check")"
    printf 'Valid command output: %s\n' "$output" | tee -a "$log"
    if [[ "$output" != "Hello, AOT check!" ]]; then
        echo "Representative command returned unexpected output." >&2
        exit 1
    fi
    {
        printf '$'
        printf ' %q' "$@" goodbye "AOT check"
        printf '\n'
    } | tee -a "$log"
    output="$("$@" goodbye "AOT check")"
    printf 'Goodbye command output: %s\n' "$output" | tee -a "$log"
    if [[ "$output" != "Goodbye, AOT check!" ]]; then
        echo "Goodbye command returned unexpected output." >&2
        exit 1
    fi
    {
        printf '$'
        printf ' %q' "$@" greet
        printf '\n'
    } | tee -a "$log"
    if "$@" greet >>"$log" 2>&1; then
        status=0
    else
        status=$?
    fi
    echo "Missing-input exit code: $status" | tee -a "$log"
    if [[ "$status" != 2 ]]; then
        echo "Representative command returned an unexpected missing-input exit code." >&2
        exit 1
    fi
}

verify_example_help_tree() {
    local log="$1"
    local variant="$2"
    shift 2
    local output
    local expected_root
    local expected_child
    if [[ "$variant" == "Declarative" ]]; then
        expected_root=$'commands (Choose a greeting command.)\n├── greet (Greet someone.)\n└── goodbye (Say goodbye to someone.)'
        expected_child=$'commands (Choose a greeting command.)\n├── ... other commands omitted...\n└── goodbye (Say goodbye to someone.)'
    else
        expected_root=$'commands\n├── greet\n└── goodbye'
        expected_child=$'commands\n├── ... other commands omitted...\n└── goodbye'
    fi

    output="$("$@" --help-tree)"
    printf 'Root help tree:\n%s\n' "$output" | tee -a "$log"
    if [[ "$output" != "$expected_root" ]]; then
        echo "$variant root help tree did not match." >&2
        exit 1
    fi
    output="$("$@" goodbye --help-tree)"
    printf 'Selected help tree:\n%s\n' "$output" | tee -a "$log"
    if [[ "$output" != "$expected_child" ]]; then
        echo "$variant selected help tree did not match." >&2
        exit 1
    fi
}

for variant in Declarative Manual; do
    example="Commands.$variant"
    project="examples/02-Commands/$variant/$example.csproj"
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
    verify_example_commands "$evidence/$example.log" dotnet "$trimmed/$example.dll"
    verify_example_directives "$evidence/$example.log" "$example" dotnet "$trimmed/$example.dll"
    verify_example_help_tree "$evidence/$example.log" "$variant" dotnet "$trimmed/$example.dll"
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
    verify_example_commands "$evidence/$example.log" "$native/$example"
    verify_example_directives "$evidence/$example.log" "$example" "$native/$example"
    verify_example_help_tree "$evidence/$example.log" "$variant" "$native/$example"
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
    if [[ "$mode" == "managed" ]]; then
        results_directory="$managed_results"
    else
        results_directory="$native_results"
    fi
    mapfile -t reports < <(find "$results_directory" -maxdepth 1 -type f -name '*.trx')
    if (( ${#reports[@]} != 1 )); then
        echo "Expected exactly one $mode AOT test TRX report." >&2
        exit 1
    fi

    dotnet tool run governance -- test-results --trx "${reports[0]}" \
        --output "$evidence/$test_name-$mode-evidence.json"
    jq -e '
        (.Tests | length == 6)
        and (.Tests | all(.Status == "Passed" and .Passed == 1))
        and (.Features | any(.Id == "FEAT-09" and .Status == "Passed" and .Passed == 1))
        and ([.Requirements[]
            | select(.Id == "FEAT-09-006" or .Id == "FEAT-09-007" or .Id == "FEAT-09-008")
            | select(.Status == "Passed" and .Passed == 1)] | length == 3)
        and (.Features | any(.Id == "FEAT-13" and .Status == "Passed" and .Passed == 2))
        and (.Features | any(.Id == "FEAT-15" and .Status == "Passed" and .Passed == 1))
        and (.Features | any(.Id == "FEAT-16" and .Status == "Passed" and .Passed == 1))
        and (.Features | any(.Id == "FEAT-17" and .Status == "Passed" and .Passed == 1))
        and (.Requirements | any(.Id == "FEAT-17-004" and .Status == "Passed" and .Passed == 1))
        and ([.Requirements[]
            | select((.Id | startswith("FEAT-13-")) and .Id != "FEAT-13-003")
            | select(.Status == "Passed" and .Passed == 1)] | length == 3)
        and (.Requirements | any(.Id == "FEAT-13-003" and .Status == "Passed" and .Passed == 2))
        and ([.Requirements[]
            | select(.Id | startswith("FEAT-15-"))
            | select(.Status == "Passed" and .Passed == 1)] | length == 3)
        and ([.Requirements[]
            | select(.Id == "FEAT-16-004")
            | select(.Status == "Passed" and .Passed == 1)] | length == 1)
    ' "$evidence/$test_name-$mode-evidence.json" >/dev/null
done

sha256sum "$native/$test_name" | tee -a "$evidence/$test_name.log"

bash scripts/verify-conversion-examples.sh "$evidence" "$compiler"
bash scripts/verify-multicall-examples.sh "$evidence" "$compiler"

echo "Trimmed examples and managed/NativeAOT test evidence: $evidence"
