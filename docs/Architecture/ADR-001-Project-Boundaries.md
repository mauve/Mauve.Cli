# ADR-001: Source-generation-first CLI parser boundaries

## Status

Accepted bootstrap direction. Public parser APIs and behavior remain planned.

## Context

Maz demonstrates typed generated command declarations, explicit runtime
definitions, command hierarchies, completion and reusable OptionPacks.
Its Azure requests, application configuration and rendering are not generic
CLI responsibilities. Runtime discovery/reflection would complicate trimming,
NativeAOT and deterministic validation.

## Decision

- `Mauve.Cli` targets .NET 11 and owns the future explicit command model,
  parsing, diagnostics and invocation contracts.
- `Mauve.Cli.Generators` is a Roslyn incremental-generator scaffold targeting
  `netstandard2.0` for compiler-host compatibility. It is an analyzer
  project reference, never a runtime application dependency.
- The primary user experience is declarative, generated registration and
  binding. The explicit model remains supported with equivalent semantics;
  neither path may discover application assemblies by runtime scanning.
- MSTest tests execute on .NET 11 under VSTest. Generator tests may use
  Roslyn compilations; neither generator host targeting nor the .NET 10
  Governance tooling changes the product's .NET 11 requirement.
- OptionPacks own reusable typed composition. Inherited/global visibility
  is separate. Pack values must be isolated between parses/invocations.
- Both examples are honest scaffolds until the parser exists. Their
  initial NativeAOT gates establish only the compiler/toolchain wiring.
- Product assemblies are not publishable packages yet (`IsPackable=false`).
  Distribution/package layout is future work, not an accidental empty NuGet
  package contract.

## Consequences

There is no implementation/API compatibility promise in this bootstrap.
Avoid speculative interfaces and failing/skipped placeholder tests. Resolve
documented grammar/API blockers with the owner before implementing affected
requirements. Future AOT checks must retain and exercise actual generated
and explicit-model code; an empty runtime reference can be trimmed away.

Affected requirements include
[FEAT-01](../Requirements/FEAT-01-Command-Model.md),
[FEAT-07](../Requirements/FEAT-07-Parse-Results-And-Diagnostics.md),
[FEAT-11](../Requirements/Planned/FEAT-11-Declarative-Api-And-Generation.md),
[FEAT-12](../Requirements/FEAT-12-Inherited-And-Global-Options.md),
[FEAT-13](../Requirements/Planned/FEAT-13-NativeAot-And-Trimming.md) and
[FEAT-14](../Requirements/Planned/FEAT-14-OptionPacks.md).
