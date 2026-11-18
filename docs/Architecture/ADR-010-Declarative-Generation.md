# ADR-010: Declarative command generation

## Status

Accepted for the initial generated-model API.

## Context

The CLI needs an AOT-friendly declaration path that builds the same immutable
command model as the explicit builder API. Declaration names, supported shapes
and generator diagnostics had not previously been selected.

## Decision

- Place declaration attributes in `Mauve.Cli.Declarations`: `CliCommand`,
  `CliChild`, `CliOption`, `CliArgument` and `CliHandler`.
- Require top-level, non-generic partial command classes, explicitly ordered
  child inclusions, and explicitly ordered partial get-only properties.
  Require handlers to be instance methods with no parameters, one
  `CancellationToken`, or one `CommandInvocationContext`, and a return shape
  supported by FEAT-09.
- Generate one deterministic partial source per command and expose
  `CreateModel` and `CreateBuilder` on each declared root. Generated handlers
  directly construct the selected command, attach an invocation-scoped
  binding context, and call the declared method. Runtime assembly scanning
  is not used.
- Use generator-only diagnostics `MCLI001` for invalid command declarations,
  `MCLI002` for invalid members, and `MCLI003` for invalid hierarchy or
  visible-name collisions. Diagnostics contain invariant guidance, not
  declared literal values. An invalid root emits no root registration source.
- Keep unsupported declaration features explicit errors. The initial API does
  not infer defaults from property initializers or nullability, and does not
  emit arbitrary converter/factory callbacks. Callers can still supply an
  explicit `ConverterRegistry` to the shared parser.
- Preserve ordered immutable runtime definitions and expose the same typed
  values to generated handlers and explicit-model handlers.
- Declare generated packs with `CliOptionPack`, `CliPack` and
  `CliPackOption`. Generated root builders may be extended with explicit
  root-owned options before the shared `CommandModel.Create` validation.
  Generated handlers use `CommandInvocationContext` to access pack values.

## Consequences

Generated models are build-time source and have no generator runtime
dependency. The explicit builder remains available and does not reference
Roslyn. New declaration shapes or generator diagnostic IDs require a separate
review and must retain the shared runtime validation rules.

Affected requirements:
[FEAT-11](../Requirements/FEAT-11-Declarative-Api-And-Generation.md) and
[FEAT-14](../Requirements/FEAT-14-OptionPacks.md).
