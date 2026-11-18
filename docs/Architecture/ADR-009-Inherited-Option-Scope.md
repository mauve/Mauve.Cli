# ADR-009: Explicit inherited option scope

## Status

Accepted for the parser implementation.

## Context

Command-local options must remain local by default. Applications also need
selected ancestor options available to descendants without implicitly making
every parent option global or allowing shadowing.

## Decision

- Add `OptionScope.Local` as the default and `OptionScope.Inherited` as an
  explicit declaration. Inherited options are visible on their owner and
  selected descendants only after that owner appears in the command path.
- Build effective options in selected-command-first, nearest-inherited-
  ancestor order. Validate each reachable command's effective canonical
  names and aliases before parsing; reject collisions rather than selecting
  one definition by precedence.
- Recognize ancestor-local options before descending only so they can be
  diagnosed as `OptionOutOfScope`; do not bind or convert them on the final
  descendant. A descendant option is not visible before selecting it.
- Retain the declaring owner and token provenance in parsed values. Expose
  inherited options once through parsing, help and invocation, with help
  grouping keyed by owner and stable group key.
- Keep OptionPack membership independent of scope; pack options must opt in
  explicitly when that feature is integrated.

## Consequences

Option visibility is branch-local and deterministic. Reusing the same local
name in disjoint branches remains valid; all simultaneous visible conflicts
fail model creation.

Affected requirement:
[FEAT-12](../Requirements/FEAT-12-Inherited-And-Global-Options.md).
