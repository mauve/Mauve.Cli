# FEAT-14: OptionPacks

## Status
**PARTIAL**

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
Generated and explicit parity remains
[planned](Planned/FEAT-14-OptionPacks.md).

## Requirements
### FEAT-14-001 - Typed composition
Allow commands to include reusable packs containing ordered options and nested packs. Expose a scoped invocation view for reading pack values.
**Acceptance:** A command includes a network pack with a nested retry pack; its handler reads both typed values, and an empty pack is valid.
**Security:** A pack adds no implicit global scope, configuration access or service execution.

### FEAT-14-002 - Composition integrity
Validate nested composition for cycles, duplicate inclusion keys and duplicate visible names or aliases before parsing.
**Acceptance:** A self-inclusion and a collision across two packs fail deterministically; the same pack can be reused on disjoint commands.
**Security:** Snapshot traversal is bounded by the configured model node and nesting limits.

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

Generated declaration parity, generated pack accessors, and completion data
views are not implemented here. Completion and providers remain owned by
FEAT-10, and generated composition remains planned under FEAT-14-003.

## Metrics Impact
**Instruments:** none - option-pack composition adds no runtime instrumentation.

## Verification
Focused tests cover nested values, help grouping, empty packs, cycles,
collisions, disjoint reuse and concurrent/sequential isolation. The local
package-free harness passed these scenarios. Product MSTest and Governance
evidence remains unavailable until the trusted local Governance package feed
is restored.

## Diagrams

```mermaid
flowchart LR
    Command["Command"] --> Network["Network inclusion"]
    Network --> Retry["Retry inclusion"]
    Retry --> Values["Invocation-local parsed values"]
```
