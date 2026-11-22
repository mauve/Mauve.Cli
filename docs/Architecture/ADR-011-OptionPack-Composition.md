# ADR-011: Explicit option-pack composition

## Status

Accepted for explicit and generated composition. The generated declaration
form below replaces the earlier generic pack attributes as a breaking API
change. Observed verification for the replacement is recorded in the
[validation ledger](../validation-gaps.md).

## Context

Reusable option groups need stable inclusion identity, predictable validation,
help grouping and invocation-local typed access. Pack options must remain
ordinary definitions governed by existing scope, conversion and result rules.

## Decision

- Compose packs with `OptionPackBuilder.AddOption` and ordered
  `OptionPackBuilder.AddPack(key, pack, title, description)` calls. Commands
  include packs with the same explicit key/title contract through
  `CommandBuilder.AddPack`.
- Snapshot builder state during `CommandModel.Create`. Reject cyclic graphs,
  duplicate sibling inclusion keys, duplicate visible option names/aliases,
  and configured depth/node-limit violations before parsing.
- Flatten each pack depth-first through its nested inclusions, then append its
  own options. Command-owned options precede packs. A pack title groups its
  own options; the nearest titled ancestor applies to untitled descendants.
  An option's explicit `OptionDisplayGroup` takes precedence.
- Keep each inclusion path in immutable model metadata. A handler resolves a
  pack by its key and owner command through `CommandInvocationContext`, then
  reads only definitions in that view. No lookup by CLR type or global tree
  search is provided.
- Keep bound values in the ordinary per-parse `ParseResult`; the pack
  blueprint and static definitions never store mutable invocation values.
- The explicit builder API retains its inclusion keys and the existing
  runtime context key contract. Generated declarations do not ask users for
  inclusion keys.
- Declare a generated pack as a `[CliOptionPack(Title = ..., Description = ...)]`
  partial class, with `[CliOption]` partial properties for its options.
  Constructors on commands and packs declare direct nested pack dependencies
  by type; a direct pack type can appear only once for a given parent. A pack
  type may still be reused on separate branches.
- Every generated command and pack must have exactly one instance constructor.
  A parameterless constructor is supported; if parameters are declared, each
  must be a declared pack dependency. Reject other parameter types and
  ambiguous multiple-constructor shapes rather than resolving arbitrary
  services.
- Derive generated inclusion identity deterministically from the pack type
  and keep it internal. Generated code resolves through the established
  runtime context contract; do not add public type-based lookup or a general
  dependency-injection mechanism.
- Construct a fresh pack graph per invocation, recursively constructing and
  binding nested packs before invoking a parent constructor. Attach each
  instance's own scoped context after its constructor returns. Constructors
  receive declared pack dependencies, not arbitrary services.
- Infer option order from source order and ordinary option arity from property
  type. Generated root builders continue to expose `CreateBuilder()` so
  callers can add explicit root-owned options before shared
  `CommandModel.Create` validation. Generated and explicit paths share runtime
  validation and parsing.

## Consequences

Pack reuse is safe across disjoint command branches and concurrent parses.
Pack options gain no inherited scope unless each option explicitly requests
it. Help grouping is a projection and does not mutate shared option metadata.
The generated declaration change intentionally removes the unreleased
`CliPack<TPack>` and `CliPackOption<TValue>` forms; existing verification for
those forms does not demonstrate acceptance of the replacement.

Affected requirement:
[FEAT-14](../Requirements/FEAT-14-OptionPacks.md).
