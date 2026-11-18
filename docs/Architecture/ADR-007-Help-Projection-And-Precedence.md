# ADR-007: Sanitized help projection and precedence

## Status

Accepted for the parser implementation.

## Context

Help must be useful to hosts without invoking parser callbacks or leaking
sensitive values. Parsing a help request must preserve the ordinary input
grammar's structural failures while bypassing validation work that is
irrelevant to displaying the selected command's usage.

## Decision

- Project a validated model and optional command into an immutable
  `HelpDocument` before rendering. Reject command definitions outside the
  model and commands marked hidden.
- Remove controls, Unicode line/paragraph separators and bidi controls from
  display metadata before exposing it. Keep titles and labels single-line;
  normalize description CRLF/CR to LF and preserve LF as plain paragraph
  breaks.
- Exclude hidden options and positionals. Show a default only when explicit
  display text and an active default policy are present, the definition is
  not required, and it is not sensitive. Never evaluate a factory.
- Use deterministic plain-text output with LF endings, 80-column default
  width, explicit width of at least 20 columns, and explicit wrapping.
  Custom renderers receive only the sanitized projection; output sink faults
  propagate to the caller.
- Identify option groups by the selected command's canonical path and stable
  group key. Keep path components separate from the key. Ungrouped options
  remain in a group scoped to their owner and use the reserved `@ungrouped`
  key.
  Repeated group keys on one command must agree on title and description.
- When enabled, install the marked `--help`/`-h` flag on each command in the
  model snapshot until general inherited options are implemented. Any
  spelling collision is a model error.
- Scan ordinary token structure and validate the helper's boolean literal
  before considering help. Unknown/out-of-scope options, missing values,
  excess positionals and duplicate scalar options remain failures. A true
  help occurrence with no such errors returns `HelpRequested` before
  conversion, requiredness/default handling or application validators.

## Consequences

Applications control when and where to render help through
`HelpDocument.Create`, `HelpTextRenderer.Render` and `HelpOutput.Write`.
`HelpRequested` has no typed values and is never a dispatchable result.
General inherited-option behavior and flattened OptionPack ordering remain
owned by FEAT-12 and FEAT-14.

Affected requirement:
[FEAT-08](../Requirements/FEAT-08-Help-And-Usage.md).
