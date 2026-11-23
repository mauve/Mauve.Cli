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
Validate nested composition for cycles, duplicate inclusion keys in the explicit builder model, duplicate direct pack types in generated declarations, and duplicate visible names or aliases before parsing.
**Acceptance:** A self-inclusion, repeated direct pack type, or collision across two packs fails deterministically; the same pack type can be reused on disjoint commands.
**Security:** Snapshot traversal is bounded by the configured model node and nesting limits.

### FEAT-14-003 - Generated and explicit parity
Declare packs as `[CliOptionPack(Title = ..., Description = ...)]` partial
classes whose `[CliOption]` get/init auto-properties declare options. Command and
pack constructors declare nested pack dependencies by pack type; a direct
dependency type may occur only once for a given command or pack. Infer option
order from source position and option arity from the property type. Generated
pack identity is a stable internal key derived from its type, not a
user-authored declarative key. Generate a model that uses the shared runtime
validation and invocation behavior.
**Acceptance:** Equivalent nested packs created through generated and explicit
APIs bind the same values and help groups; duplicate direct pack types and
cyclic dependencies produce generator errors without root registration; an
option added to the generated root builder works alongside generated packs.
**Security:** Neither construction path bypasses definition validation or
converter registration rules, and generated declarations never store
invocation values in static state or resolve arbitrary constructor services.

### FEAT-14-004 - Help integration
Project pack titles into help groups without mutating option metadata.
**Acceptance:** Nested titled packs form separate groups in child-first option order; an untitled child inherits the nearest titled parent. Generated packs infer Title from XML summary and Description from remarks unless the corresponding attribute argument is explicit, including empty or null values.
**Security:** Pack titles and descriptions pass through the existing help sanitizer.

### FEAT-14-005 - Per-invocation isolation
Keep values and mutable collections in each parse result, not reusable pack definitions or shared process state.
**Acceptance:** Sequential parses and concurrent invocations read their own pack values, and collection values are distinct per parse.
**Security:** Reusing a pack blueprint cannot expose another invocation's values.

## Design
Generated pack display metadata can come from XML documentation, as specified
by FEAT-11-006. Summary is the short title and remarks the longer description.
This changes metadata sources only: root help already includes visible packed
options, and child help includes ancestor pack options only when their options
declare inherited scope. Empty or hidden-only pack visibility is unchanged.

`OptionPackBuilder` composes immutable `OptionDefinition` instances and nested
packs. In the explicit builder API, a command or nested pack gives each
inclusion a stable key; keys are unique among siblings. `CommandModel.Create`
snapshots the composition, rejects cycles and collisions, and applies the
configured model bounds. These explicit builder keys and the public runtime
context key contract do not change.

Flattening preserves command options first, then pack inclusion order; each
nested pack is visited before its parent's own options. The snapshot retains
the inclusion tree for owner-scoped `OptionPackContext` access. Parsed values
remain in `ParseResult`, keyed by their declaring command and option
definition. An option's explicit display group wins; otherwise the nearest
titled pack supplies its group.

The generated declaration form replaces `CliPack<TPack>` and
`CliPackOption<TValue>`: `[CliOptionPack(Title = ..., Description = ...)]`
marks a partial pack class and `[CliOption]` marks its get/init option
properties. Command and pack constructors declare direct nested-pack
dependencies by type. A parent cannot declare the same direct pack type more
than once; reuse on separate branches remains valid. Generated declarations
have no user-facing inclusion key. The generator assigns a stable internal
key derived from pack type identity, while the existing runtime context key
contract remains unchanged.

Generated pack options expose typed properties and use the shared immutable
definitions and invocation-local parsed values. Every pack instance is fresh
per invocation. Pack dependencies are constructed recursively and their
values bound before the parent constructor runs; each pack's own properties
are initialized after its constructor. Constructor dependencies are limited to
declared pack types; this is not general dependency injection. A generated
root still exposes `CreateBuilder()` so callers can add explicit root-owned
options before `CommandModel.Create` validates the combined model.
Completion and providers remain owned by FEAT-10.

Pack inclusion order follows constructor parameter order; option order follows
compilation syntax-tree order and source span.
Pack option `bool` values infer BooleanFlag, `IReadOnlyList<T>` values infer
greedy repeatable collection semantics, and other values infer RequiredScalar
unless an explicit named arity override is supplied. The replacement is an
approved breaking declarative API change; earlier strict-suite results for the
previous form are not acceptance evidence for this design.

## Metrics Impact
**Instruments:** none - option-pack composition adds no runtime instrumentation.

## Verification
Focused tests cover nested values, generated/explicit parity, constructor
dependencies, inferred names and arity, manual root options, help grouping,
empty packs, cycles, collisions, disjoint reuse and concurrent/sequential
isolation. Observed MTP evidence is recorded in the validation ledger.
XML-derived pack titles/descriptions and explicit overrides are additionally
verified by the FEAT-11-006 generator tests in the passing 132-test Release MTP
run; no scope or visibility rule changed.

## Diagrams

```mermaid
flowchart LR
    Command["Command"] --> Network["Network inclusion"]
    Network --> Retry["Retry inclusion"]
    Retry --> Values["Invocation-local parsed values"]
```
