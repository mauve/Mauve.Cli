# FEAT-14: OptionPacks

## Status
**ACTIVE**

## Capability
Compose reusable option definitions with nested inclusion, help grouping and
invocation-local typed access.

## Context
Depends on [FEAT-02](FEAT-02-Options-And-Arguments.md),
[FEAT-04](FEAT-04-Typed-Conversion.md),
[FEAT-07](FEAT-07-Parse-Results-And-Diagnostics.md), and
[FEAT-09](FEAT-09-Invocation-Lifecycle.md). Help integration follows
[FEAT-08](FEAT-08-Help-And-Usage.md); inherited option scope follows
[FEAT-12](FEAT-12-Inherited-And-Global-Options.md). Composition decisions
are recorded in [ADR-011](../Architecture/ADR-011-OptionPack-Composition.md).
Generated pack declarations and explicit-model parity are defined by
FEAT-14-003 and [ADR-010](../Architecture/ADR-010-Declarative-Generation.md).

## Requirements
### FEAT-14-001 - Typed composition
Allow commands to include reusable packs containing ordered options and nested packs. Expose a scoped invocation view for reading pack values.
**Acceptance:** A command includes a network pack with a nested retry pack; its handler reads both typed values, and an empty pack is valid.
**Security:** A pack adds no implicit global scope, configuration access or service execution.

### FEAT-14-002 - Composition integrity
Validate nested composition for cycles, duplicate inclusion keys and duplicate visible names or aliases before parsing.
**Acceptance:** A self-inclusion and a collision across two packs fail deterministically; the same pack can be reused on disjoint commands.
**Security:** Snapshot traversal is bounded by the configured model node and nesting limits.

### FEAT-14-003 - Generated and explicit parity
Declare reusable packs with `CliOptionPack`, generic `CliPack<TPack>` and generic `CliPackOption<TValue>`, infer inclusion/option order from source position and option arity from the value type, and generate a model that uses the shared runtime validation and invocation behavior.
**Acceptance:** Equivalent nested packs created through generated and explicit APIs bind the same values and help groups; invalid pack aliases and cycles produce generator errors without root registration; an option added to the generated root builder works alongside generated packs.
**Security:** Neither construction path bypasses definition validation or converter registration rules, and generated declarations never store invocation values in static state.

### FEAT-14-004 - Help integration
Project pack titles into help groups without mutating option metadata.
**Acceptance:** Nested titled packs form separate groups in child-first option order; an untitled child inherits the nearest titled parent.
**Security:** Pack titles and descriptions pass through the existing help sanitizer.

### FEAT-14-005 - Per-invocation isolation
Keep values and mutable collections in each parse result, not reusable pack definitions or shared process state.
**Acceptance:** Sequential parses and concurrent invocations read their own pack values, and collection values are distinct per parse.
**Security:** Reusing a pack blueprint cannot expose another invocation's values.

## Design
`OptionPackBuilder` composes immutable `OptionDefinition` instances and nested
packs. A command or nested pack gives each inclusion a stable key; keys are
unique among siblings. `CommandModel.Create` snapshots the composition,
rejects cycles and collisions, and applies the configured model bounds.

Flattening preserves command options first, then pack inclusion order; each
nested pack is visited before its parent's own options. The snapshot retains
the inclusion tree for owner-scoped `OptionPackContext` access. Parsed values
remain in `ParseResult`, keyed by their declaring command and option
definition. An option's explicit display group wins; otherwise the nearest
titled pack supplies its group.

Generated pack options expose immutable definition handles. Generated
handlers that accept `CommandInvocationContext` resolve a pack by its inclusion
key and read values from the current invocation. A generated root also exposes
`CreateBuilder()` so callers can add explicit root-owned options before
`CommandModel.Create` validates the combined model. Completion and providers
remain owned by FEAT-10.

Pack inclusion keys and pack-option names remain explicit. Pack and option
order follows compilation syntax-tree order and source span. Pack option
`bool` values infer BooleanFlag, `IReadOnlyList<T>` values infer greedy
repeatable collection semantics, and other values infer RequiredScalar unless
an explicit named arity override is supplied.

## Metrics Impact
**Instruments:** none - option-pack composition adds no runtime instrumentation.

## Verification
Focused tests cover nested values, generated/explicit parity, generic
attributes, inferred source order and arity, nullable versus explicitly empty
pack metadata, manual root options, help grouping, empty packs, cycles,
collisions, disjoint reuse and concurrent/sequential isolation. Observed MTP
evidence is recorded in the validation ledger.

## Diagrams

```mermaid
flowchart LR
    Command["Command"] --> Network["Network inclusion"]
    Network --> Retry["Retry inclusion"]
    Retry --> Values["Invocation-local parsed values"]
```
