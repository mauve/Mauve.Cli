# ADR-010: Declarative command generation

## Status

Accepted and revised for inferred declaration metadata.

## Context

The CLI needs an AOT-friendly declaration path that builds the same immutable
command model as the explicit builder API. Declaration names, supported shapes
and generator diagnostics had not previously been selected.

## Decision

- Infer omitted display metadata from Roslyn XML documentation at build time:
  command summary/remarks become short/long descriptions, property summaries
  become descriptions, and pack summary/remarks become title/description.
  Explicit attribute arguments, including null and empty strings, win.
  Do not expand external includes or inherited documentation.
- Extract one-line shell `<example><code>` entries in XML order. The optional
  `language` attribute defaults to shell; other languages remain ordinary
  documentation and do not become help invocations. Emit ordinary builder
  metadata and keep XML processing out of runtime code. Invalid shell entries
  use MCLI002 rather than silently rewriting source text.

- Place declaration attributes in `Mauve.Cli.Declarations`: `CliCommand`,
  `CliChild`, `CliParent`, `CliOption`, `CliArgument` and `CliHandler`.
- Use generic `CliChild<TCommand>` attributes instead of `Type` constructor
  arguments for command-child declarations. OptionPack declarations follow
  [ADR-011](ADR-011-OptionPack-Composition.md).
- Permit `[CliParent<TParent>]` on a child as the inverse declaration.
  Normalize both forms into one graph, order sibling edges by the compilation
  syntax tree and attribute span, reject all declared cycles, and report
  `MCLI003` on both attributes when an edge uses both forms.
- Require top-level, non-generic partial command classes and get/init
  auto-properties for bound command and pack members. Standalone directive
  values remain partial get-only properties. Infer order from compilation syntax-tree order and source
  span, including declarations split across partial class files.
- Infer omitted command, option and positional names as kebab-case C#
  identifiers, stripping a trailing `Command` suffix from command types.
  Explicit names override inference. Generated pack inclusion keys are
  internal and type-derived, not user-authored; pack option names are inferred
  as prefix-free long names from their `[CliOption]` property names unless the
  declaration supplies a short-only `ShortName`. `Aliases` contains
  prefix-free long aliases; `ShortName` and `ShortAliases` contain ASCII short
  characters. Explicit builder keys and runtime
  context keys remain unchanged, as specified by ADR-011.
- Infer `bool` options as flags, `IReadOnlyList<T>` options as greedy
  repeatable collections, other options as required-value scalars, scalar
  positionals as `1..1`, and collection positionals as `1..N`. Named arity
  and positional-bound overrides remain available and must validate against
  the declared type. `OptionArity.Counting` is an explicit `int`/`int?`
  override whose use is enabled by parser/host syntax configuration; it is
  never inferred from an integer property.
- Infer option requiredness and positional minimum from C# `required` and the
  exact DataAnnotations `RequiredAttribute`, unless a named CLI override is
  supplied. Use Roslyn to evaluate supported constant initializers into typed
  parser defaults and invariant help display metadata. Required defaults stay
  inert and sensitive defaults stay hidden under FEAT-06 and FEAT-08.
- Preserve omitted optional display metadata as `null`; explicit empty text is
  not rewritten to omission.
  Require handlers to be instance methods with no parameters, one
  `CancellationToken`, or one `CommandInvocationContext`, and a return shape
  supported by FEAT-09.
- Generate one deterministic partial source per command and expose
  `CreateModel` and `CreateBuilder` on each declared root. Generated handlers
  directly construct the selected command, initialize its properties from the
  validated parse result, and call the declared method. No command object is
  created for parse-only, help or completion. Runtime assembly scanning is not
  used; handlers can request the invocation context for presence and origin.
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
  infer optional-scalar token syntax from nullability or execute nonconstant
  initializer expressions. Static converter providers, generated enum maps and union
  factories are now supported under
  [ADR-015](ADR-015-Converter-Ownership-And-Unions.md); arbitrary
  reflection-discovered callbacks remain unsupported. Callers can still
  supply an explicit host `ConverterRegistry`.
- Preserve ordered immutable runtime definitions and expose the same typed
  values to generated handlers and explicit-model handlers.
- Lower declaration-level validation constraints and static providers to
  shared model validators as specified by
  [ADR-017](ADR-017-Declarative-Validation.md).
- Lower declarative option environment-binding metadata to the shared
  `OptionDefinition` schema under
  [ADR-016](ADR-016-Environment-Option-Binding.md); generated code does not
  read process environment variables.
- **Superseded for OptionPack declarations by
  [ADR-011](ADR-011-OptionPack-Composition.md):** use
  `[CliOptionPack(Title = ..., Description = ...)]` partial pack classes with
  `[CliOption]` get/init option properties, and declare nested pack dependencies
  in command/pack constructors. Every generated command and pack must have
  exactly one instance constructor; a parameterless constructor is supported,
  while constructor parameters may only be declared pack dependencies.
  Unsupported parameter types or ambiguous multiple constructors are invalid
  members (MCLI002) and suppress root registration. The generator constructs
  and binds nested packs before parent constructors and initializes each
  instance's own properties after its constructor. Generated root builders continue to
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
including FEAT-11-005, FEAT-11-006 and FEAT-11-007, and
[FEAT-14](../Requirements/FEAT-14-OptionPacks.md), and
[FEAT-16](../Requirements/FEAT-16-Environment-Option-Binding.md).
