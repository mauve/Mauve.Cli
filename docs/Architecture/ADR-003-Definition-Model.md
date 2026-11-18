# ADR-003: Immutable command and input definition model

## Status

Accepted for the parser implementation.

## Context

The planned command and option requirements need a stable validated snapshot
before parsing. Command metadata may be assembled as a graph, but parser state,
current values, and mutable application objects must not live in reusable
definitions. The model also needs to expose the same invariants to explicit
registration and source generation.

## Decision

- Applications assemble a command graph through a mutable `CommandBuilder`.
  The builder is an input description only; it is not retained by the
  validated model.
- `CommandModel.Create` validates and copies the reachable graph into an
  immutable snapshot. The root display label is not an argv routing token.
- Every command in a snapshot has one canonical ancestry. Reusing the same
  builder object under two parents is invalid; separately constructed
  definitions with equal names on disjoint branches are valid.
- Command and option lookup uses ordinal, case-sensitive ASCII spellings.
  Hidden definitions participate in all collisions and validation.
- The snapshot preserves declaration order and exposes immutable path
  components rather than joined strings as identity.
- Option and positional definitions carry explicit value type, arity,
  nullability, visibility, sensitivity, and descriptive metadata. Runtime
  parsing values and callbacks belong to later feature contracts.
- Model validation uses bounded iterative traversal. It rejects cycles,
  repeated ancestry, invalid names, local collisions, unsupported positional
  arity and incompatible option declarations before any argv processing.
- Runtime and generated definitions must use the same schema and validation
  rules; the generator may report an invalid declaration at compile time but
  must not produce a valid-looking partial model.

## Consequences

The initial explicit API exposes `CommandBuilder`, `OptionDefinition`,
`PositionalDefinition`, and `CommandModel`. These names are public API choices,
not compatibility promises for an unreleased package. Builders remain mutable
for convenient declaration and cycle detection, while model snapshots own
defensive copies and can be safely reused.

The model does not resolve inherited scopes, pack inclusion identity,
converters, defaults, help, or completion by itself. Those features extend
this schema through their owning requirements and must reuse its validation
invariants.

Affected requirements:
[FEAT-01 active definitions](../Requirements/FEAT-01-Command-Model.md),
[FEAT-01 planned command selection](../Requirements/Planned/FEAT-01-Command-Model.md),
and [FEAT-02](../Requirements/FEAT-02-Options-And-Arguments.md).
