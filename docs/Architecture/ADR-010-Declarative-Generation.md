# ADR-010: Declarative command generation

## Status

Accepted and revised for inferred declaration metadata.

## Context

The CLI needs an AOT-friendly declaration path that builds the same immutable
command model as the explicit builder API. Declaration names, supported shapes
and generator diagnostics had not previously been selected.

## Decision

- Place declaration attributes in `Mauve.Cli.Declarations`: `CliCommand`,
  `CliChild`, `CliOption`, `CliArgument` and `CliHandler`.
- Use generic `CliChild<TCommand>` attributes instead of `Type` constructor
  arguments for command-child declarations. OptionPack declarations follow
  [ADR-011](ADR-011-OptionPack-Composition.md).
- Require top-level, non-generic partial command classes and partial get-only
  bound properties. Infer order from compilation syntax-tree order and source
  span, including declarations split across partial class files.
- Infer omitted command, option and positional names as kebab-case C#
  identifiers, stripping a trailing `Command` suffix from command types.
  Explicit names override inference. Generated pack inclusion keys are
  internal and type-derived, not user-authored; pack option names are inferred
  from their `[CliOption]` property names. Explicit builder keys and runtime
  context keys remain unchanged, as specified by ADR-011.
- Infer `bool` options as flags, `IReadOnlyList<T>` options as greedy
  repeatable collections, other options as required-value scalars, scalar
  positionals as `1..1`, and collection positionals as `1..N`. Named arity
  and positional-bound overrides remain available and must validate against
  the declared type.
- Preserve omitted optional display metadata as `null`; explicit empty text is
  not rewritten to omission.
  Require handlers to be instance methods with no parameters, one
  `CancellationToken`, or one `CommandInvocationContext`, and a return shape
  supported by FEAT-09.
- Generate one deterministic partial source per command and expose
  `CreateModel` and `CreateBuilder` on each declared root. Generated handlers
  directly construct the selected command, attach an invocation-scoped
  binding context, and call the declared method. Runtime assembly scanning
  is not used.
- Make each valid generated root implement `IGeneratedCliCommand`, whose
  static model-factory contract is satisfied by the generated `CreateModel`.
  Implement `ISynchronousGeneratedCliCommand` only when every handler in the
  root's reachable command tree has a `void` or `int` return type. The
  generic constraint on `CliInvoker.Run<TCommand>` then rejects async
  descendants at compile time; `RunAsync<TCommand>` accepts every generated
  root.
- Use generator-only diagnostics `MCLI001` for invalid command declarations,
  `MCLI002` for invalid members, and `MCLI003` for invalid hierarchy or
  visible-name collisions. Diagnostics contain invariant guidance, not
  declared literal values. An invalid root emits no root registration source.
- Keep unsupported declaration features explicit errors. The API does not
  infer defaults from property initializers or optional-scalar token syntax
  from nullability, and does not
  emit arbitrary converter/factory callbacks. Callers can still supply an
  explicit `ConverterRegistry` to the shared parser.
- Preserve ordered immutable runtime definitions and expose the same typed
  values to generated handlers and explicit-model handlers.
- **Superseded for OptionPack declarations by
  [ADR-011](ADR-011-OptionPack-Composition.md):** use
  `[CliOptionPack(Title = ..., Description = ...)]` partial pack classes with
  `[CliOption]` partial option properties, and declare nested pack dependencies
  in command/pack constructors. Every generated command and pack must have
  exactly one instance constructor; a parameterless constructor is supported,
  while constructor parameters may only be declared pack dependencies.
  Unsupported parameter types or ambiguous multiple constructors are invalid
  members (MCLI002) and suppress root registration. The generator constructs
  and binds nested packs before parent constructors and attaches each
  instance's own context afterward. Generated root builders continue to
  permit explicit root-owned options before shared `CommandModel.Create`
  validation.

## Consequences

Generated models are build-time source and have no generator runtime
dependency. The explicit builder remains available and does not reference
Roslyn. This revision intentionally breaks the earlier unreleased
`Type`/order/required-arity attribute constructors rather than retaining
parallel declaration forms. New declaration shapes or generator diagnostic
IDs require a separate review and must retain the shared runtime validation
rules.

Affected requirements:
[FEAT-11](../Requirements/FEAT-11-Declarative-Api-And-Generation.md),
including FEAT-11-005, and
[FEAT-14](../Requirements/FEAT-14-OptionPacks.md).
