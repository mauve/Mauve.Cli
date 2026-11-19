# ADR-011: Explicit option-pack composition

## Status

Accepted for explicit and generated composition.

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
- Generated declarations use `CliOptionPack`, `CliPack<TPack>` and
  `CliPackOption<TValue>`, with source-derived order and type-derived ordinary
  arity. Generated root builders expose `CreateBuilder()` so
  callers can add explicit root-owned options before validation. Generated
  handlers use `CommandInvocationContext` to resolve pack views by inclusion
  key. Generated and explicit paths share runtime validation and parsing.

## Consequences

Pack reuse is safe across disjoint command branches and concurrent parses.
Pack options gain no inherited scope unless each option explicitly requests
it. Help grouping is a projection and does not mutate shared option metadata.

Affected requirement:
[FEAT-14](../Requirements/FEAT-14-OptionPacks.md).
