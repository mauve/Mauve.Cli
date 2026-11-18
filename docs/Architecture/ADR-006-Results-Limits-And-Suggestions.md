# ADR-006: Bounded results and advisory suggestions

## Status

Accepted for the parser implementation.

## Context

The parser already returns immutable outcome and diagnostic objects, but
resource bounds, diagnostic ordering and misspelling guidance must be
consistent across all parses and safe for untrusted argv.

## Decision

- Store limits in an immutable `CliLimits` instance captured by each
  `CommandModel`. Defaults bound argv count and size, command depth, options
  per command, model nodes and diagnostics; callers may select other positive
  bounds explicitly.
- Check argv count before reading tokens, then scan token and aggregate UTF-16
  lengths in order. A violation stops before routing or callbacks and yields
  `InputLimitExceeded` with no selected path.
- Check model depth, local option count and command/option/positional node
  count before snapshot arrays are allocated. Invalid command graphs are
  rejected by `CommandModel.Create`.
- Collect at most the configured diagnostic count. When another error would
  exceed it, use the final slot for `DiagnosticLimitExceeded`.
- Order structural diagnostics by token index and ordinal category, then
  conversion errors by supplying token, missing-required errors by definition
  order, value-validation errors by definition/registration order, and
  cross-validation errors by registration order.
- Suggest only visible declared names in the current routing scope. Inspect
  at most 256 spellings, compare at most 128 code units with ordinal matching
  and edit distance at most two, and return at most five unique canonical
  names. Prefix matches are ranked first. Suggestions never alter parse
  outcome.
- Keep unmatched text available only through the explicit `UnmatchedTokens`
  property. Safe messages never interpolate input text, failure identifiers
  or callback exception messages.

## Consequences

`ParseDiagnostic` includes immutable suggestions and precise token
coordinates. A parser instance and model can be reused concurrently because
all parse state and diagnostic buffers are local to one invocation. The
limits are safety bounds, not performance measurements.

Affected requirements:
[FEAT-07](../Requirements/FEAT-07-Parse-Results-And-Diagnostics.md),
[FEAT-05](../Requirements/FEAT-05-Cardinality-And-Special-Forms.md), and
[FEAT-06](../Requirements/FEAT-06-Defaults-And-Validation.md).
