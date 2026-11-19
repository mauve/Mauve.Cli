# Contributing

Read [the repository instructions](docs/AGENT_INSTRUCTIONS.md), the
[feature lifecycle](docs/Requirements/README.md), and the applicable feature
and architecture documents before changing product behavior.

## Feature changes

Keep proposals in `docs/Requirements/Planned/`. Promote a requirement only
with its implementation, focused tests, an active feature owner, and an entry
in `docs/validation-gaps.md`. Keep each requirement ID in one feature
document.

Tests use MSTest and method-level Governance metadata:

```csharp
[TestMethod]
[TestProperty(Metadata.Feature, "FEAT-01")]
[TestProperty(Metadata.Requirement, "FEAT-01-001")]
public void CreatePreservesCommandOrder()
{
    // Assert observable behavior.
}
```

A declared test, a source index, or a skipped run is not passing evidence.
Record actual test results and environment-gated checks separately. Keep
callbacks, parse values, and invocation state isolated.

Do not edit files listed in `.mauve-governance/manifest.json`. Do not add
blanket analyzer suppressions, install global tools, or modify the sibling
Mauve.Governance checkout.

## Build and test

Use .NET SDK 11.0.100. If the local Governance feed is missing, build it from
a trusted checkout, then run the repository gate:

```sh
bash scripts/bootstrap-governance.sh ../Mauve.Governance
bash scripts/verify.sh
```

For focused work, build and test with:

```sh
dotnet build src/Mauve.Cli/Mauve.Cli.csproj --configuration Release
dotnet test tests/Mauve.Cli.Tests/Mauve.Cli.Tests.csproj --configuration Release
dotnet test tests/Mauve.Cli.Generators.Tests/Mauve.Cli.Generators.Tests.csproj --configuration Release
dotnet test tests/Mauve.Cli.Aot.Tests/Mauve.Cli.Aot.Tests.csproj --configuration Release
```

All test projects use Microsoft Testing Platform. Use MTP options such as
`--report-trx` directly with `dotnet test`; do not use VSTest `--logger`
syntax.

NativeAOT verification requires a native C/C++ toolchain and platform
development libraries. The current verified target is Linux x64 with Clang:

```sh
bash scripts/verify-aot.sh
```

ARM64 and other RIDs are unverified. A cross-publish without executing the
target binary is not a passing result. Keep package lock changes intentional,
run locked restore after dependency changes, and use one commit per feature
or logical unit.
