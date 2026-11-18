# Feature specification lifecycle

This editable guide starts from a Mauve.Governance template. Adapt it to this
repository without weakening the installed
[policy](../../governance.md) or the
[original Mauve.Governance Policy](https://github.com/mauve/Mauve.Governance/blob/main/src/templates/docs/governance.md).
It is not manifest-owned; updates do not replace it.

## Active features

All requirements of [FEAT-01](FEAT-01-Command-Model.md), all four
requirements of [FEAT-02](FEAT-02-Options-And-Arguments.md), FEAT-03-001,
FEAT-03-002, FEAT-03-003, FEAT-03-004 and all requirements of
[FEAT-04](FEAT-04-Typed-Conversion.md),
[FEAT-05](FEAT-05-Cardinality-And-Special-Forms.md) and
[FEAT-06](FEAT-06-Defaults-And-Validation.md), and all of
[FEAT-07](FEAT-07-Parse-Results-And-Diagnostics.md),
[FEAT-08](FEAT-08-Help-And-Usage.md), and
[FEAT-09](FEAT-09-Invocation-Lifecycle.md), and
[FEAT-11](FEAT-11-Declarative-Api-And-Generation.md), and
[FEAT-12](FEAT-12-Inherited-And-Global-Options.md), and
[FEAT-14](FEAT-14-OptionPacks.md) have active implementation owners.
The remaining features stay in the [planned index](Planned/README.md). Focused tests are
declared for the active requirements, but no product test-run evidence has
yet been recorded for this implementation.

Keep implemented and partially implemented features in
`docs/Requirements/FEAT-<id>-<name>.md`. Each document records Status,
Capability, Context, Requirements, Metrics Impact, Verification and Diagrams.
Each active requirement has a unique `### FEAT-<id>-<requirement>` heading
with explicit **Acceptance:** and **Security:** evidence. Link its validation
method in `docs/validation-gaps.md`; a documented test is not proof it ran.

Two optional sections, `Design` then `Implementation Plan`, may appear once
each between `Requirements` and `Metrics Impact`; no other extra sections are
accepted. A minimal skeleton: `# FEAT-<id>: <title>`, then `## Status`
(**PLANNED** for drafts), `## Capability`, `## Context`, `## Requirements`
(with `### FEAT-<id>-<n> - <title>` plus **Security:** and **Acceptance:**
lines), `## Metrics Impact` (with an `**Instruments:**` line: `none - <reason>`
or backticked names plus Emission points, Tags, Privacy, Dashboards and Tests
labels; optional `**Diagnostics:**` lines list backticked codes, and names
and codes must be unique across documents), `## Verification`, `## Diagrams`.

## Planned work and decisions

Keep proposals and unimplemented requirements in `Planned/` with an explicit
**PLANNED** status. Promote a requirement or whole feature to its active
owner in the same change as implementation, behavioral tests, traceability,
metrics/diagnostics, architecture and the applicable gate. Keep remaining
drafts under `Planned/` without reusing requirement IDs. Record material
decisions in `docs/Architecture/ADR-ZZZ-*.md` and link affected requirements.
Document retirement and supersession rather than presenting planned or
retired work as current evidence.

The Tool validates present lifecycle files and basic active/planned document
structure; it does not validate promotion evidence or test coverage.
