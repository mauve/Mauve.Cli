# ADR-014: Root-owned directive pipeline

## Status

Accepted for the directive feature.

## Context

Directives are bracketed, may carry ordered bare values, and must execute
before the selected command handler. They are not options because their
occurrence syntax and execution stage are distinct, and they are not commands
because they do not alter the selected command path. Runtime type discovery
would violate the generated/explicit parity and NativeAOT boundary in
[ADR-001](ADR-001-Project-Boundaries.md).

## Decision

- Store ordered directive definitions on the immutable command model root and
  make that root-owned list available throughout its command tree.
- Require explicit root inclusion for standalone generated directive types;
  root method directives are declared directly on the root command.
- Parse directive spans before the first child-command token, then select the
  command path and parse ordinary input. Directives may occur among root
  options while command selection remains open, but are not recognized after
  selecting a child, consuming a positional, or seeing `--`.
- Validate every directive occurrence before invoking any directive callback.
  Keep valid directive results separate from ordinary typed parse values so
  they remain available when ordinary parsing requests help or reports
  invalid input.
- Run validated directives sequentially before returning the ordinary parse
  outcome or dispatching the selected command handler. A directive's explicit
  stop result takes precedence over that pending ordinary outcome.
- Use direct generated construction and binding; do not scan assemblies,
  reflect over application members, or load the generator at runtime.
- Project directive metadata through help and completion without constructing
  directive instances or executing callbacks.

## Consequences

The parser owns bracket-span structure and value binding, while the invoker
owns ordered execution and short-circuiting. All-or-nothing validation avoids
side effects from earlier valid directives when a later directive is
malformed. Directive value definitions remain distinct from `OptionDefinition`
even when generator declarations reuse `[CliOption]` as the property marker.

The feature adds bounded parsing work and generated direct references. Help,
completion, explicit-model parity, trimming, and NativeAOT evidence must be
updated together with the feature implementation. The decision's behavior is
specified by [FEAT-15](../Requirements/FEAT-15-Directives.md).
