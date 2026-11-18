# ADR-011: Explicit option-pack composition

## Status

Accepted for the explicit-model implementation.

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
- Generated declarations do not yet include pack inclusions. Generator and
  explicit-model parity remains planned as FEAT-14-003.

## Consequences

Pack reuse is safe across disjoint command branches and concurrent parses.
Pack options gain no inherited scope unless each option explicitly requests
it. Help grouping is a projection and does not mutate shared option metadata.

Affected requirement:
[FEAT-14](../Requirements/FEAT-14-OptionPacks.md).
