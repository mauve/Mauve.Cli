# Repository agent instructions

Read [the feature lifecycle](Requirements/README.md), the
[planned backlog](Requirements/Planned/README.md), [ADR-001](Architecture/ADR-001-Project-Boundaries.md)
and [POL-01](Policies/POL-01-Strict-Dotnet-And-Aot.md) before implementation.
This repository contains an implemented parser, invocation API, source
generator and explicit and generated OptionPacks. Check the active feature
documents for supported behavior; completion and end-to-end NativeAOT
verification remain planned.

Use .NET 11/C# 15, with a source-generation-first declarative API and an
equivalent explicit model. Keep the Roslyn component build-time-only and
the runtime AOT-compatible. OptionPacks are a first-class typed composition
feature, separate from inherited/global option scope. Do not copy maz's
Azure, HTTP, renderer or configuration services into this library.

Resolve marked feature design blockers before implementing their behavior.
Promote only implemented requirements into active ownership, preserving IDs,
with focused behavioral tests, ledger entries, relevant ADRs and checks.
Do not treat planned documents, source traceability or a disposable runner
probe as product MSTest or Governance acceptance evidence. Record what
actually ran and what was blocked in the validation ledger.

Use native method-level MSTest metadata:
`[TestProperty(Metadata.Feature, "FEAT-01")]` and
`[TestProperty(Metadata.Requirement, "FEAT-01-001")]`, importing
`Mauve.Governance.Testing.MSTest` and MSTest explicitly. Feature/requirement
IDs must have active owners when tests implement them. Preserve assembly
parallelization and isolate invocation state. Do not add trivial placeholders
merely to replace the honest empty baseline.

Run `bash scripts/bootstrap-governance.sh /path/to/Mauve.Governance` first
when the local feed is missing, then `bash scripts/verify.sh`.
Use locked restore normally; update central package versions/locks
intentionally. No global tool installation,
blanket analyzer suppressions or managed-file edits.

Paths in `.mauve-governance/manifest.json` belong to Governance. Put local
rules in unmanaged documents and nested configs, never the managed root
`.editorconfig`. Record usage findings in [governance-feedback.md](governance-feedback.md),
including reproductions and verification limits. Do not modify the sibling
Governance checkout as part of parser work.

Native scaffolds prove only bootstrap/toolchain compatibility. Later examples
must exercise real generated/explicit APIs before claiming parser AOT support.
Hosted workflows remain opt-in; no protected/hosted run is currently claimed.
