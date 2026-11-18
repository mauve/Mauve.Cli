# Planned feature designs

This editable guide starts from a Mauve.Governance template. Proposed
requirements are not active behavior or passing evidence. Keep each
requirement ID in one owning document and move it to the active feature
specification only with its implementation and verification. See the
[feature lifecycle](../README.md) and the
[original Mauve.Governance Policy](https://github.com/mauve/Mauve.Governance/blob/main/src/templates/docs/governance.md).
This file is not manifest-owned and may be adapted to the repository.

## Planned parser backlog

All requirements listed here are **PLANNED**. All requirements of
[FEAT-01](../FEAT-01-Command-Model.md),
[FEAT-02](../FEAT-02-Options-And-Arguments.md) and
[FEAT-03](../FEAT-03-Token-Grammar.md), and all of
[FEAT-04](../FEAT-04-Typed-Conversion.md),
[FEAT-05](../FEAT-05-Cardinality-And-Special-Forms.md) and
[FEAT-06](../FEAT-06-Defaults-And-Validation.md),
[FEAT-07](../FEAT-07-Parse-Results-And-Diagnostics.md), and
[FEAT-08](../FEAT-08-Help-And-Usage.md), and
[FEAT-09](../FEAT-09-Invocation-Lifecycle.md), and
[FEAT-11](../FEAT-11-Declarative-Api-And-Generation.md), and
[FEAT-12](../FEAT-12-Inherited-And-Global-Options.md) are active. FEAT-14 is
partially active; only generated pack parity remains planned. The source-generation-first direction and
explicit-model equivalence are recorded in the
[project boundaries decision](../../Architecture/ADR-001-Project-Boundaries.md);
[enrollment](../../Architecture/ADR-002-Governance-Enrollment.md) describes
Governance, not parser behavior.

1. [FEAT-10 Completion](FEAT-10-Completion.md)
2. [FEAT-13 NativeAOT and Trimming](FEAT-13-NativeAot-And-Trimming.md)
3. [FEAT-14 Generated pack parity](FEAT-14-OptionPacks.md)

## Staging and decisions

The implemented sequence covers the model and definitions, parsing and
conversion, cardinality, defaults/validation, parse results/diagnostics,
help, invocation, command-path routing and inherited option scope. Next
implement completion, generated pack parity and trimming/NativeAOT scenarios as prerequisites
permit. Dependencies are linked in each feature; this order is not evidence
that a stage has passed its required gate.

The documents record the approved behavior baselines, including name
comparison, option ordering, optional values, repetitions, negation, short
clusters, collection boundaries, shadowing, help precedence, and shell
adapters. The examples describe required behavior; implementation must not
silently replace these choices with maz behavior.

## Self-contained implementation handoff

The feature documents contain the extracted behavior, proposed replacement
contracts, state/data schemas, algorithms, boundary cases and acceptance
matrices. **A coding agent must not need the maz checkout to implement them.**
References to maz are historical provenance only. Dependencies are other
documents in this repository, not a requirement to reproduce maz's quirks.
Each Design section explicitly records intentional differences.

Public API naming is accepted for generation by
[ADR-010](../../Architecture/ADR-010-Declarative-Generation.md). Remaining API
choices for completion and packs must be recorded before those features are
implemented.

| Decision group | Approved behavior baseline / owning features |
| --- | --- |
| Names and model | Ordinal case-sensitive ASCII routing names with a separate root display label; immutable snapshots, unique ancestry and deliberate children-plus-positionals precedence (01/02). |
| Token routing | One position-aware left-to-right walk, final-scope recheck, equals-only optional values, numeric negative-value exception (03/12). |
| Cardinality | Explicit negation only, boolean true/false space consumption before child/path routing, reject scalar repeats, append atomic collections, no short clusters/stacking (05). |
| Values/defaults | Defined invariant built-in conversions, explicit converter precedence, independent absent/bare policies, no silent fault/default fallback (04/06). |
| Results/safety | Typed outcome/provenance/coordinate semantics, proposed numeric bounds, deterministic errors and advisory suggestions (07). |
| Help/invocation | Opt-in help helper with structural-error precedence, explicit non-dispatch outcomes, host-owned exceptions/process exit (08/09). |
| Completion | Logical-token cursor API, scope-safe provider/context lookup, explicit bounded failures, Bash, Zsh, Fish and PowerShell adapters (10). |
| Generation | Partial-class/property declarations, explicit semantic order metadata, instance-scoped binding, direct handlers and explicit-model parity (11; ADR-010). |
| Packs | Inclusion-key identity, child-first flattening, nearest-title grouping and invocation-local views (14; ADR-011); generated parity remains planned. |
| Compatibility | Real generated/explicit trimming/AOT scenarios; Linux ARM64 as first proposed product-verification row, other RIDs unverified (13). |

### Shared terminology and ownership

- **Definition / snapshot:** immutable validated metadata. Reusable across
  parses; never a holder of current values. Identity includes canonical owner
  path and pack inclusion path where relevant.
  Declaring-owner identities do not change when inherited; per-operation views
  also include the selected path. Blueprint identity for cycle checks is
  separate from inclusion identity for values (12/14).
- **Selected path:** root plus resolved canonical descendants; argv omits the
  executable name. Identity uses a component sequence, not a joined string;
  alias input never changes canonical identity.
- **Occurrence:** one recognized option spelling or positional token, with its
  original zero-based argv coordinates. Bare, explicit empty and absent differ.
- **Effective order:** selected command's own options and flattened packs,
  then inherited ancestors nearest-first. It controls stable diagnostics/
  callback order and the input to flat help-group ordering, never shadowing
  precedence or a requirement to render all options globally ungrouped (08/12/14).
- **Validated / HelpRequested / Invalid:** only Validated has dispatchable
  typed values. HelpRequested bypasses conversion/default/validator callbacks
  and is not invocation; completion uses its own partial-context operation.
  Optional no-default entries may remain unavailable on Validated; this is
  distinct from invalid or available-null entries.
- **Application callback fault:** an unexpected converter/factory/validator/
  constructor/binder/handler exception, propagated to the host without default message
  logging or success-shaped fallback. Expected returned failures are structured
  input errors. Completion has its own explicit ProviderFailed boundary.
- **Isolation:** new parses and high-level invocations get fresh mutable
  containers/views. Arbitrary custom reference graphs are application-owned;
  no automatic deep cloning or resource disposal is promised.

Implementers may choose internal structures/names that preserve these
contracts. They must not resolve an explicit product-choice blocker by
copying external source. Keep all acceptance scenarios planned until actual
implementation, test metadata, active ownership and observed run evidence
exist.
