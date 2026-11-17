# Planned feature designs

This editable guide starts from a Mauve.Governance template. Proposed
requirements are not active behavior or passing evidence. Keep each
requirement ID in one owning document and move it to the active feature
specification only with its implementation and verification. See the
[feature lifecycle](../README.md) and the
[original Mauve.Governance Policy](https://github.com/mauve/Mauve.Governance/blob/main/src/templates/docs/governance.md).
This file is not manifest-owned and may be adapted to the repository.

## Planned parser backlog

All entries and their requirements are **PLANNED**. No parser behavior or
test-run evidence is active. The source-generation-first direction and
explicit-model equivalence are recorded in the
[project boundaries decision](../../Architecture/ADR-001-Project-Boundaries.md);
[enrollment](../../Architecture/ADR-002-Governance-Enrollment.md) describes
Governance, not parser behavior.

1. [FEAT-01 Command Model](FEAT-01-Command-Model.md)
2. [FEAT-02 Options and Arguments](FEAT-02-Options-And-Arguments.md)
3. [FEAT-03 Token Grammar](FEAT-03-Token-Grammar.md)
4. [FEAT-04 Typed Conversion](FEAT-04-Typed-Conversion.md)
5. [FEAT-05 Cardinality and Special Forms](FEAT-05-Cardinality-And-Special-Forms.md)
6. [FEAT-06 Defaults and Validation](FEAT-06-Defaults-And-Validation.md)
7. [FEAT-07 Parse Results and Diagnostics](FEAT-07-Parse-Results-And-Diagnostics.md)
8. [FEAT-08 Help and Usage](FEAT-08-Help-And-Usage.md)
9. [FEAT-09 Invocation Lifecycle](FEAT-09-Invocation-Lifecycle.md)
10. [FEAT-10 Completion](FEAT-10-Completion.md)
11. [FEAT-11 Declarative API and Generation](FEAT-11-Declarative-Api-And-Generation.md)
12. [FEAT-12 Inherited and Global Options](FEAT-12-Inherited-And-Global-Options.md)
13. [FEAT-13 NativeAOT and Trimming](FEAT-13-NativeAot-And-Trimming.md)
14. [FEAT-14 OptionPacks](FEAT-14-OptionPacks.md)

## Staging and decisions

Implement model and definitions, then grammar, conversion and cardinality;
follow with defaults/validation and results. Add generation and packs, then
help, inheritance, invocation and completion as their prerequisites permit.
Finally publish and run representative generated and explicit applications
under trimming and NativeAOT. Dependencies are linked in each feature; this
order is not evidence that any stage has been completed.

The documents identify proposed rules and design blockers, especially name
comparison, option ordering, optional values, repetitions, negation, short
clusters, collection boundaries, shadowing, help precedence, public API and
shell adapters. Resolve each relevant blocker before implementing it; the
examples illustrate proposed outcomes, not approved public APIs.

## Self-contained implementation handoff

The feature documents contain the extracted behavior, proposed replacement
contracts, state/data schemas, algorithms, boundary cases and acceptance
matrices. **A coding agent must not need the maz checkout to implement them.**
References to maz are historical provenance only. Dependencies are other
documents in this repository, not a requirement to reproduce maz's quirks.
Each Design section explicitly records intentional differences.

All features and the detailed Design baselines remain proposals. The earlier
approval established feature boundaries and source-generation-first direction,
not every newly spelled-out token rule or public API. Resolve the relevant
choices below with the product owner, recording the decision in the owning
feature/ADR rather than silently reverting to maz behavior.

| Decision group | Proposed baseline / owning features |
| --- | --- |
| Names and model | Ordinal case-sensitive ASCII routing names with a separate root display label; immutable snapshots, unique ancestry and deliberate children-plus-positionals precedence (01/02). |
| Token routing | One position-aware left-to-right walk, final-scope recheck, equals-only optional values, numeric negative-value exception (03/12). |
| Cardinality | Explicit negation only, boolean true/false space consumption before child/path routing, reject scalar repeats, append atomic collections, no short clusters/stacking (05). |
| Values/defaults | Defined invariant built-in conversions, explicit converter precedence, independent absent/bare policies, no silent fault/default fallback (04/06). |
| Results/safety | Typed outcome/provenance/coordinate semantics, proposed numeric bounds, deterministic errors and advisory suggestions (07). |
| Help/invocation | Opt-in help helper with structural-error precedence, explicit non-dispatch outcomes, host-owned exceptions/process exit (08/09). |
| Completion | Logical-token cursor API, scope-safe provider/context lookup, explicit bounded failures, no initial shell adapters (10). |
| Generation | Bounded partial-class/property declarations, explicit semantic order metadata, instance-scoped binding, static callbacks and explicit-model parity (11). |
| Packs | Inclusion-key identity, child-first flattening, nearest-title grouping and invocation-local views (14). |
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
