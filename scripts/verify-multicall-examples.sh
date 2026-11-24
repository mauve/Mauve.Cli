#!/usr/bin/env bash
set -euo pipefail
ulimit -c 0

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
evidence="$1/multicall-examples"
compiler="${2:-clang}"
mode="${3:-all}"
mkdir -p "$evidence"
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}"

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

check_output() {
    local log="$1"
    local expected="$2"
    shift 2
    local output
    local errors="$log.stderr"
    {
        printf '$'
        printf ' %q' "$@"
        printf '\n'
    } >>"$log"
    output="$("$@" 2>"$errors")"
    printf '%s\n' "$output" >>"$log"
    if [[ "$output" != "$expected" || -s "$errors" ]]; then
        cat "$errors" >>"$log"
        echo "Multicall output did not match; see $log." >&2
        exit 1
    fi
}

verify_links() {
    local log="$1"
    local directory="$2"
    local binary="$3"
    local name
    for name in multicall suite farewell smurf unmatched; do
        ln -s "$binary" "$directory/$name"
    done
    check_output "$log" "Hello, Ada!" "$directory/multicall" greet Ada
    check_output "$log" "Hello, Ada!" "$directory/suite" hi Ada
    check_output "$log" "Goodbye, Ada!" "$directory/farewell" Ada
    check_output "$log" "Hello, Ada!" "$directory/smurf" Ada
    check_output "$log" "Good day, Ada." "$directory/smurf" formal Ada

    local help
    help="$(cd "$directory" && ./smurf --help)" 2>"$log.stderr"
    printf '%s\n' "$help" >>"$log"
    if [[ "$help" != *"Usage: ./smurf [OPTIONS] <name>"* || -s "$log.stderr" ]]; then
        echo "Mapped multicall help did not match; see $log." >&2
        exit 1
    fi
    local status
    set +e
    "$directory/unmatched" Ada >"$log.unmatched.stdout" 2>"$log.unmatched.stderr"
    status=$?
    set -e
    printf 'Unmatched executable exit code: %s\n' "$status" >>"$log"
    cat "$log.unmatched.stderr" >>"$log"
    if [[ "$status" == 0 || -s "$log.unmatched.stdout" ]] \
        || ! grep -q "does not match any configured multicall route" "$log.unmatched.stderr"; then
        echo "Unmatched multicall invocation did not fail closed; see $log." >&2
        exit 1
    fi
    if find "$directory" -iname '*Mauve.Cli.Generators*' -print -quit | grep -q .; then
        echo "Multicall publication contains the build-time generator." >&2
        exit 1
    fi
    sha256sum "$directory/$binary" >>"$log"
    if [[ -f "$directory/$binary.dll" ]]; then
        sha256sum "$directory/$binary.dll" >>"$log"
    fi
}

sha256sum scripts/verify-multicall-examples.sh \
    src/Mauve.Cli/Invocation/CliMulticallRoute.cs \
    src/Mauve.Cli/Invocation/CliInvoker.cs \
    src/Mauve.Cli/Parsing/CliParser.cs \
    src/Mauve.Cli/Help/HelpDocument.cs \
    src/Mauve.Cli/Help/HelpExampleExpander.cs \
    docs/Requirements/FEAT-17-Multicall-Dispatch.md \
    docs/Architecture/ADR-018-Multicall-Dispatch.md \
    examples/17-Multicall/Declarative/Program.cs \
    examples/17-Multicall/Declarative/Multicall.Declarative.csproj \
    examples/17-Multicall/Declarative/packages.lock.json \
    examples/17-Multicall/Manual/Program.cs \
    examples/17-Multicall/Manual/Multicall.Manual.csproj \
    examples/17-Multicall/Manual/packages.lock.json >"$evidence/source-hashes.txt"

for variant in Declarative Manual; do
    example="Multicall.$variant"
    binary="multicall-${variant,,}"
    project="examples/17-Multicall/$variant/$example.csproj"
    log="$evidence/$example.log"
    run_logged "$log" dotnet restore "$project" --locked-mode
    managed="$evidence/$example-managed"
    run_logged "$log" dotnet build "$project" -c Release --no-restore -p:PublishAot=false
    run_logged "$log" dotnet publish "$project" -c Release --no-restore \
        -p:BuildProjectReferences=false -p:PublishAot=false -p:PublishTrimmed=false \
        -p:SelfContained=false --output "$managed"
    verify_links "$log" "$managed" "$binary"
    if [[ "$mode" == "--managed-only" ]]; then
        continue
    fi

    trimmed="$evidence/$example-trimmed"
    run_logged "$log" dotnet publish "$project" -c Release -r linux-x64 --no-restore \
        -p:BuildProjectReferences=false --output "$trimmed" \
        -p:PublishAot=false -p:PublishTrimmed=true -p:TrimMode=link \
        -p:SelfContained=true -p:EnableTrimAnalyzer=true -p:ILLinkTreatWarningsAsErrors=true
    verify_links "$log" "$trimmed" "$binary"

    native="$evidence/$example-native"
    run_logged "$log" dotnet publish "$project" -c Release -r linux-x64 --no-restore \
        -p:BuildProjectReferences=false --output "$native" \
        -p:PublishAot=true -p:PublishTrimmed=true -p:EnableTrimAnalyzer=true \
        -p:ILLinkTreatWarningsAsErrors=true -p:IlcTreatWarningsAsErrors=true \
        "-p:CppCompilerAndLinker=$compiler"
    verify_links "$log" "$native" "$binary"
done

echo "Multicall real-symlink example checks passed: $evidence"
