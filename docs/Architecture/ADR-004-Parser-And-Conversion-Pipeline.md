# ADR-004: Position-aware parsing and explicit conversion

## Status

Accepted for the parser implementation.

## Context

The command model needs a deterministic parser that preserves argv token
boundaries, does not execute or re-tokenize input, and keeps application
callbacks behind structural validation. Value conversion must not depend on
ambient culture or runtime type discovery.

## Decision

- Parse the caller-provided argument vector from left to right. Preserve
  original token indices and recognize `--` as the permanent end of option
  and command selection.
- Match only declared option names and aliases. Split inline values at the
  first `=`; do not infer short clusters or consume an option-like token as a
  value, except for the documented negative-numeric candidate.
- Resolve child commands only from unconsumed bare tokens while command
  selection is open. Repeated text in a value or positional remains data.
- Recheck option ownership after selecting the final command. Reject
  out-of-scope occurrences instead of silently inheriting options.
- Do not invoke converters when structural diagnostics exist. Convert
  accepted values in argv order; returned failures are safe structured
  diagnostics, while thrown callback faults propagate to the host.
- Use invariant numeric parsing by default. An explicitly selected culture
  may change only the decimal separator. Enums require an explicit ordinal
  name map; arbitrary runtime parsing and type scanning are not used.
- Keep parser results per operation. Invalid results expose diagnostics and
  caller-inspectable unmatched tokens, but no partial typed values.

## Consequences

`CliParser` consumes an already-tokenized argument vector. Applications own
shell quoting and token construction. The parser does not perform response
file expansion, environment substitution, command execution or fallback
dispatch. A future inherited-option feature may alter visibility only through
its own explicit scope model; parser routing does not infer inheritance.

Affected requirements:
[FEAT-01](../Requirements/FEAT-01-Command-Model.md),
[FEAT-03](../Requirements/FEAT-03-Token-Grammar.md),
[FEAT-04](../Requirements/FEAT-04-Typed-Conversion.md),
[FEAT-05](../Requirements/FEAT-05-Cardinality-And-Special-Forms.md),
[FEAT-06](../Requirements/FEAT-06-Defaults-And-Validation.md),
and [FEAT-07](../Requirements/FEAT-07-Parse-Results-And-Diagnostics.md).
