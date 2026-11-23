# ADR-017: Generated validation and source coordinates

## Status

Accepted for the shared validation pipeline.

## Context

Declarative commands need the same validation semantics as explicit models,
including item-specific failures after collection conversion. A flat list of
token indices cannot identify which repeated or inline value supplied an item.
Runtime reflection would also undermine trimming and NativeAOT.

## Decision

- Lower declared range, length, nonempty, allowed-value and custom-provider
  attributes to typed validators on ordinary immutable definitions. Numeric
  range bounds are inclusive and use invariant numeric or temporal literals;
  unsupported and malformed declarations fail generation.
- Static cross-value methods receive directly bound typed property values.
  Bind by case-insensitive property name and exact type, including visible
  OptionPack members. Skip the method if a required bound value is unavailable.
  A returned parameter target is resolved to the definition handle.
- Retain parse-local source coordinates per logical value, including original
  option/value indices, inline start and value length. A failure can target a
  collection item and UTF-16 offset inside its source value. Defaults and
  environment values never acquire synthetic argv coordinates.
- Translate only `CliValidationException` to a validation diagnostic. All
  other callback faults continue to propagate unchanged. Rejection codes,
  targets, item indices and offsets are validated. An explicit outcome
  `message` is an opt-in public rejection reason, not exception text. Bound
  it to 512 UTF-16 code units and sanitize it to one line before display.
  Suppress reasons for sensitive or hidden targets; authors must never
  include secrets or raw input in public messages. Built-in rules explain
  their declared constraints; item failures include a one-based item number.
- Project the immutable allowed-string validator metadata to bounded
  completion and sanitized help. Hidden or sensitive definitions never
  suggest values; sensitive values do not appear in help.

## Consequences

Generated and explicit definitions share execution, ordering, limits and
error semantics. Rules retain the existing default, conversion, help and
completion stage boundaries. Arbitrary custom reference values remain
application-owned. No runtime assembly scanning or dynamic code is required.

Affected requirements: [FEAT-06-005 and FEAT-06-006](../Requirements/FEAT-06-Defaults-And-Validation.md),
[FEAT-11-008](../Requirements/FEAT-11-Declarative-Api-And-Generation.md),
[FEAT-07-002](../Requirements/FEAT-07-Parse-Results-And-Diagnostics.md), and
[FEAT-10-001](../Requirements/FEAT-10-Completion.md).
