# ADR-016: Invocation-time environment option binding

## Status

Accepted for the environment option binding feature.

## Context

The parser consumes caller-owned argv and already distinguishes explicit
presence, defaults, conversion, validation and diagnostics. Rewriting
environment variables as argv tokens would fabricate coordinates and make
environment content participate in command syntax. Runtime reflection or a
general configuration-provider dependency would also violate the parser's
deterministic, AOT-friendly boundary.

## Decision

- Keep public `CliParser.Parse` environment-free. Apply environment
  resolution only through `CliInvoker` host configuration.
- Keep environment enablement off by default. Each option has an immutable
  tri-state eligibility setting; an inherited setting uses the host's
  eligibility default.
- Resolve a full explicit option environment name as-is. Otherwise normalize
  the configured prefix (or root command name) and canonical long name by
  replacing hyphens with underscores and uppercasing invariantly. Short-only
  options need explicit names.
- Use an injectable lookup with process-environment lookup as the default.
  Null means absent and empty string means supplied. Resolve only options
  absent from argv; explicit values are never merged with lower-precedence
  values.
- Apply defaults < environment < argv. Environment values satisfy required
  option presence and carry `ValueOrigin.Environment`, `WithValue` presence
  and no argv indices. Convert them through the existing converter and
  validation pipeline.
- Encode collections using strict CSV with configurable delimiter,
  doubled-quote escapes, and preserved empty fields. Do not add a package
  dependency or shell-like expansion. Apply the model's existing token-length
  and argument-count limits to environment values and collection fields.
- Report malformed environment values with existing safe diagnostics and no
  raw value or invented argv coordinate. Propagate lookup and application
  callback faults. Do not read environment values for help, completion or
  structurally invalid argv.
- When host processing is enabled, project each eligible environment-variable
  name inline with its option in default help. Omit these names when
  processing is disabled and never perform environment lookups while
  rendering help.
- Keep generated attributes as build-time lowering to the same immutable
  `OptionDefinition`; do not add runtime discovery.

## Consequences

The host owns source selection and injection; the parser owns typed
conversion, requiredness, defaults, validation and provenance. Environment
values do not affect command selection, token grammar, shell completion or
argv inspection. An enabled host may fail on colliding selected option names;
it must not silently choose one.

Affected requirements:
[FEAT-02](../Requirements/FEAT-02-Options-And-Arguments.md),
[FEAT-06](../Requirements/FEAT-06-Defaults-And-Validation.md),
[FEAT-07](../Requirements/FEAT-07-Parse-Results-And-Diagnostics.md),
[FEAT-09](../Requirements/FEAT-09-Invocation-Lifecycle.md),
[FEAT-11](../Requirements/FEAT-11-Declarative-Api-And-Generation.md), and
[FEAT-16](../Requirements/FEAT-16-Environment-Option-Binding.md).
