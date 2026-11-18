# Contributing

Read [the repository agent instructions](docs/AGENT_INSTRUCTIONS.md), the
[feature lifecycle](docs/Requirements/README.md), and the applicable feature
and architecture documents before changing product behavior.

## Requirements and tests

Keep planned requirements in `docs/Requirements/Planned/`. Promote a
requirement only with its implementation, focused tests, an active feature
owner, and a row in `docs/validation-gaps.md`. Do not describe a planned
requirement or source traceability as passing test evidence.

Tests use MSTest and method-level Governance feature and requirement
properties. Follow the existing test project conventions and preserve test
parallelization and invocation isolation.

## Local checks

Use .NET SDK 11.0.100. The Governance package feed is built from a trusted
Mauve.Governance checkout:

```sh
bash scripts/bootstrap-governance.sh ../Mauve.Governance
bash scripts/verify.sh
```

The verification script runs the repository's Governance, restore, build,
format, test, and native scaffold checks. It fails when required tools or the
trusted package feed are unavailable; do not report skipped checks as passed.
For focused development, build the runtime with:

```sh
dotnet build src/Mauve.Cli/Mauve.Cli.csproj -c Release
```

Run the runtime and generator tests with:

```sh
dotnet test tests/Mauve.Cli.Tests/Mauve.Cli.Tests.csproj -c Release
dotnet test tests/Mauve.Cli.Generators.Tests/Mauve.Cli.Generators.Tests.csproj -c Release
```

Do not edit files listed in `.mauve-governance/manifest.json` directly. Do
not add global analyzer suppressions, install global tools, or modify the
sibling Governance checkout as part of parser work.
