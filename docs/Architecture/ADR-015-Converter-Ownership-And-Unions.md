# ADR-015: Converter ownership and C# union conversion

## Status

Accepted and implemented.

## Context

The parser and completion service previously accepted external converter
registries, but generated and explicit high-level entry points did not.
Definitions could not override conversion, and generated roots did not own
their custom-type or enum registrations. .NET 11/C# 15 adds nominal unions;
runtime constructor discovery would violate the established NativeAOT boundary.

## Decision

- Keep `ValueConverter<T>` and add `ICliValueConverter<T>` with a static
  `Convert` contract. Generated code and explicit generic registrations call
  providers directly without constructing or scanning them.
- `CliConverter<TProvider>` on a custom value type automatically registers its
  converter in each generated root reaching that type. On a bound property it
  supplies a definition-local override for the scalar or collection element.
- Explicit definitions accept a typed `ValueConverterRegistration`. Root
  builders own an immutable model-wide registry; child registrations are
  invalid. Host registries remain available on `CliParser`, `CompletionService`
  and `CliHostOptions`.
- A definition-local converter wins. Otherwise an exact target registration
  wins, followed by underlying nullable-type resolution, then built-ins.
  Model/host exact-key collisions fail before input conversion; they are not
  implicit overrides.
- Preserve intrinsic boolean-flag syntax. A custom boolean converter requires
  scalar arity, not flag arity.
- Generate exact ordinal enum maps. Explicit consumers supply their own typed
  maps. Nullable custom collections require a statically typed adapter:
  generated roots emit `RegisterNullable<T>()`; explicit consumers register it
  themselves. Never manufacture `Nullable<T>` or collection factories through
  reflective generic construction.
- Generate ordered case factories for source `union` declarations. Explicit
  consumers register `RegisterUnion<TUnion>(union => union.Case<TCase>(factory))`.
  The first successful case wins; expected failure proceeds, unexpected callback
  faults propagate immediately. All reachable case conversions must be
  supported, and recursive dependencies are rejected. Numeric cases participate
  in the existing negative-numeric routing exception.
- Case order is user-visible. Catch-all cases such as `string` belong last.
  No-match failure is `union.no-match`, without raw input. Hand-written
  `[Union]` implementations and referenced unions without source declarations
  need explicit registration or an application converter.
- Completion uses the same model/host composition and definition overrides.
  Custom callbacks execute only with explicit completion-safety opt-in.
  A union is safe only when every reachable case converter is safe.
- Expand built-ins using strict invariant formats. File/directory conversion
  constructs `FileInfo`/`DirectoryInfo` without checking existence or doing I/O.
  Applications retain responsibility for authorization, existence, links,
  traversal and filesystem-kind validation.
- Align the build-time Roslyn package with the pinned C# 15 compiler
  (`Microsoft.CodeAnalysis.CSharp` 5.12.0). This is an intentional central
  version/lock update, not a runtime dependency.

## Consequences

Models remain immutable and reusable. Host composition creates an
operation-local parser/completion service rather than mutating shared settings.
Arbitrary callback effects, reference-object ownership and concurrency remain
application responsibilities. Union conversion may try multiple callbacks;
applications should make case converters pure or explicitly account for that
ordering. C#'s generated union representation boxes value cases; this library
does not invent a different union type or claim non-boxing behavior.

The selected formats and APIs are owned by
[FEAT-04](../Requirements/FEAT-04-Typed-Conversion.md),
[FEAT-10](../Requirements/FEAT-10-Completion.md),
[FEAT-11](../Requirements/FEAT-11-Declarative-Api-And-Generation.md),
[FEAT-13](../Requirements/FEAT-13-NativeAot-And-Trimming.md), and
[FEAT-15](../Requirements/FEAT-15-Directives.md).
The language contract is documented in the
[C# union reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union).
