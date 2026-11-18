# ADR-005: Parse-local defaults and validation

## Status

Accepted for the parser implementation.

## Context

Definitions need explicit absent, bare and supplied-value behavior. Defaults
and application validators must not mutate reusable definitions, hide invalid
input, or run before structural and conversion checks have succeeded.

## Decision

- Model absent defaults as `None`, immutable `Constant`, or per-parse
  `Factory`. Do not infer defaults from nullability or runtime type; mutable
  reference defaults require a factory.
- Keep optional-option bare behavior separate from absent defaults. A bare
  occurrence has explicit provenance. Requiredness always requires an
  explicit occurrence; an absent default on a required definition is inert.
- Require collection definitions to use `IReadOnlyList<T>` and normalize
  defaults and parsed values into read-only containers. A zero-minimum rest
  positional receives a fresh empty sequence.
- Run callbacks only after a structurally valid scan. Convert explicit input
  first, then apply eligible absent defaults and bare values, run per-value
  validators in definition/registration order, and run cross-value validators
  only if value validation succeeds.
- Accumulate returned validation failures up to the diagnostic bound.
  Propagate thrown converter, factory and validator exceptions to the host;
  do not substitute values or render exception messages.
- Give validators an invocation-local read-only context with typed handles
  and explicit presence checks.

## Consequences

`OptionDefinition` and `PositionalDefinition` carry immutable default and
validator descriptors. `CommandBuilder` owns ordered cross-value validators,
which are copied into the validated command snapshot. Parse state and
collection storage are allocated per parse; arbitrary custom scalar object
graphs remain application-owned.

Affected requirements:
[FEAT-05](../Requirements/FEAT-05-Cardinality-And-Special-Forms.md),
[FEAT-06](../Requirements/FEAT-06-Defaults-And-Validation.md),
and [FEAT-07](../Requirements/FEAT-07-Parse-Results-And-Diagnostics.md).
