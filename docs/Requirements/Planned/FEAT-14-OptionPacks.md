# FEAT-14: OptionPacks

## Status
**PLANNED**

## Capability
Compose reusable typed option packs, including nested packs and
application-owned helper methods, independently of inherited/global scope.

## Context
Depends on [FEAT-02](FEAT-02-Options-And-Arguments.md),
[FEAT-04](FEAT-04-Typed-Conversion.md),
[FEAT-07](../FEAT-07-Parse-Results-And-Diagnostics.md) and
[FEAT-11](FEAT-11-Declarative-Api-And-Generation.md). Integrates with
[help](../FEAT-08-Help-And-Usage.md),
[completion](FEAT-10-Completion.md), and
[scope](../FEAT-12-Inherited-And-Global-Options.md), which own their
respective mechanisms. Evidence: `../maz/Console/Cli/OptionPack.cs`,
`../maz/Console/Cli/Shared/GlobalBehaviorOptionPack.cs`, and
`../maz/Console/Cli/Shared/SubscriptionOptionPack.cs`. Application
services, configuration precedence and maz's shared mutable parsing state
are not generic defaults.

## Requirements
### FEAT-14-001 - Typed composition
Allow a command to include reusable typed packs containing option definitions, nested packs and application-owned helpers operating on the pack's bound values.
**Acceptance:** A command includes a network pack with a nested retry pack; its helper reads that invocation's typed values; an empty pack is valid.
**Security:** Pack inclusion grants no implicit global scope, external service access or configuration authority.

### FEAT-14-002 - Composition integrity
Validate nested composition for cycles and duplicate visible names or aliases, including collisions with command options.
**Acceptance:** A pack including itself and two nested packs exposing the same alias fail deterministically; the same pack definition may be reused on disjoint commands.
**Security:** Reject ambiguous packs and cycles before traversal or invocation, without unbounded recursion.

### FEAT-14-003 - Generated and explicit parity
Support equivalent generated and explicit-model pack composition and typed value access.
**Acceptance:** Equivalent nested packs created through each path bind the same values and reject the same duplicate alias; manual-model options coexist with generated declarations under the approved integration.
**Security:** Neither construction path bypasses definition validation or converter registration rules.

### FEAT-14-004 - Help and completion integration
Expose stable pack grouping metadata and invocation-specific typed pack values to the owning help and completion mechanisms.
**Acceptance:** A titled nested pack can group its options in help; a registered completion provider can access permitted parsed pack values without a global tree search.
**Security:** Completion access is limited to the current context and must not expose sensitive values by default.

### FEAT-14-005 - Per-invocation isolation
Keep bound pack values and mutable collections isolated for each parse and invocation, even when definitions are reused concurrently.
**Acceptance:** Two simultaneous parses with different pack values and two sequential parses each observe only their own inputs; mutation in one does not alter the other.
**Security:** No cross-request data or secrets may leak through shared pack state.

## Design
#### Proposed definition and identity

A pack is a reusable definition blueprint plus an invocation-bound typed
view, not a singleton that stores the last parse's values. Metadata comprises
ordered own option definitions, ordered nested pack inclusions, optional
help title/description and explicit safe completion dependencies. Helpers
are application methods using the bound values; inclusion never executes
them, reads environment/config or grants network/service access.

Each inclusion has a stable key within its owner (for generated code, an
explicit inclusion member/key), producing identity
`declaring-command-path / inclusion-key / nested-key`. Two inclusions of the
same pack type require distinct keys; type alone is never a unique lookup
key. Definition blueprints can be reused on disjoint commands, but each
inclusion/result has its own identity and storage.
Each explicit blueprint is a materialized immutable descriptor with opaque
identity assigned at creation; generated blueprints use one descriptor
identity per pack declaration. Cycle detection uses descriptor reference/
opaque identity, not structural equality or CLR type alone. Generated recursive
type inclusions resolve to their existing descriptor identities, not endlessly
created copies. Separately configured finite explicit descriptors of the same
type can nest; depth/node limits stop even malformed lazy expansion.
User-enumerated/lazy recursive inclusion callbacks are not a supported
definition source: materialize before validation.

Keys distinguish bound views and diagnostics, not CLI spellings: they do
not automatically prefix/rename option aliases. Two same-type inclusions
exposing identical names on one command still fail collision validation.
No implicit namespacing or automatic duplicate elimination is allowed.
Renaming/remapping a reused pack's option names would need an explicit
approved definition API, not guessing from the inclusion key.

#### Flattening, validation and help grouping

Flatten top-level packs in inclusion declaration order. Within a pack visit
child packs depth-first in their declaration order, then the pack's own
normalized option list. Generated lowering places explicitly ordered generated
options before manual registrations; an explicit descriptor supplies that
same sequence directly. Command-owned options
precede packs. Freeze the resulting ordered definitions; lookup uses
identities and validated name maps, never reflection field enumeration.
Generated source ordering and manual registration points follow FEAT-11.

Detect cycles on the active inclusion/blueprint traversal stack before
flattening; a shared blueprint on separate completed branches is not itself
a cycle, but duplicated resulting visible names still fail. Reject direct
and indirect cycles with a bounded inclusion path. Validate pack-versus-pack,
pack-versus-command and inherited alias collisions through the same
FEAT-02/12 invariants, not independent dictionaries with first-wins behavior.
FEAT-07 supplies nesting/option-count limits.
Parity tests compare normalized descriptors and ordered help/default/
validator behavior, not only parsed values and collision outcomes.

Grouping is a projection, never mutation of shared option metadata.
An option's explicit group wins; otherwise the nearest enclosing titled
pack owns the group. An untitled pack lets its own ungrouped options inherit
the nearest titled ancestor. An inner titled pack preserves its own group;
the outer title must not overwrite it. Equal titles on different inclusion
paths remain distinct group identities. Titles/descriptions are inert text
sanitized by FEAT-08; no title makes an option globally visible.
Explicit option groups require a stable key plus title/description. Their
identity is `(declaring command/inclusion path, group key)`, so equal titles
never merge unrelated groups. Reusing the same key with conflicting metadata
is InvalidModel. FEAT-08 orders flat groups by first appearance in the
effective definition sequence; child-first flattening shows Retry before
Network when its options occur first.

#### Binding, lifetime and completion

Create fresh bound pack/command objects for each high-level invocation only
after a validated parse. All nested views resolve through that invocation's
result by identity. Options/collections may not store mutable current values
on the reusable blueprint, generated static field or a process-wide registry.
Default factories and custom converters obey FEAT-04/06 ownership rules;
the library does not deep-clone arbitrary application objects. New parses
must produce fresh mutable containers/defaults, including absent collections.
Only known immutable built-in constants are shareable absent defaults.
Mutable/custom reference defaults require a per-parse factory and otherwise
fail model validation; a factory returning shared mutable state violates its
application contract. Optional unavailable collections need no container;
any available collection/default storage is parse-local.

Helpers may validate application semantics or call services when explicitly
invoked by application code; those behaviors are not parser defaults and are
not run for help/completion. If helpers own resources, application factories/
invocation scopes must declare ownership; the parser does not automatically
dispose arbitrary user values. Explicit-model pack descriptors must support
the same value/access/grouping contract as generated pack declarations.

Completion resolves a declared visible inclusion path and exposes a read-only
typed data projection of permitted successfully converted prior values.
It must not instantiate arbitrary application pack constructors, call
helpers/default factories, search the whole tree by type or reveal another
branch's values. Same-type pack lookup without an inclusion identity is
ambiguous and must fail explicitly, not return the first match.
Sensitive/missing/invalid dependencies remain unavailable; FEAT-10 owns
provider invocation/failure/cancellation and completion-safe conversion.
The projection is a library-owned read-only value map with type-checked
handles keyed by owner inclusion/option identity, optionally with generated
struct accessors; it is not the application pack class.
At model validation, reject unknown/ambiguous dependency handles, sensitive/
hidden dependencies and cross-branch references. Each dependency must be
visible in every effective scope where its provider registration applies;
otherwise reject that registration rather than constructing missing packs.
A valid dependency not supplied/converted before the cursor remains unavailable.
Inherited views retain the declaring owner's inclusion identity and expose
only inherited members visible on the selected descendant (FEAT-12).

#### Worked example and acceptance assertions

Command `fetch` includes `network`, which owns `--endpoint` and includes
`retry` owning `--retries`. Help titles are Network and Retry.

| Scenario | Expected result |
| --- | --- |
| `fetch --endpoint=https://example.test --retries=2` | One network view and nested retry view read those values from this invocation only. |
| Nested Retry has a title | Its options stay in Retry, not moved into Network. |
| Two titled packs both named Display | Separate group identities, even if a renderer chooses similar headings. |
| Command also declares `--retries` | Invalid model, even if both declarations have equal defaults/types. |
| Two inclusions `primary`/`backup` of identical network blueprint | Distinct keys do not resolve duplicate CLI names; model invalid unless explicitly distinct definitions are supplied. |
| Network -> retry -> same network blueprint on active stack | Invalid cycle; bounded diagnostic, no recursive overflow. |
| Same immutable retry blueprint on two disjoint leaf commands | Valid; invocation values never shared. |
| Parallel parses with retries 2 and 5, plus an absent-default parse | Independent values/default containers; neither changes the first result. |
| Completion asks for a pack in an unrelated branch | Unavailable, never a first-by-type global lookup. |

#### Extraction, integration and approval

Maz supplies child-first enumeration, generated/manual options, typed helper
access and group titles. It mutates group/value objects and searches fields
by reflection for completion. This proposal explicitly replaces those
mechanisms with inclusion identities, projections and per-parse state.
The above is the complete owned contract; historical source links are optional.

Approve binding/declaration syntax, inclusion-key requirements and group
precedence before coding. Implement explicit immutable pack descriptors and
their validation first, then generated lowering/parity, then help/completion
integration. FEAT-11's base generator does not require packs before its first
stage, so these integration dependencies are not an excuse for a cycle.

## Metrics Impact
**Instruments:** none - planned pack composition adds no runtime metrics.

## Verification
Future tests cover nested/empty packs, explicit/generated parity, duplicate
aliases, direct and indirect cycles, help grouping, completion access and
parallel/sequential isolation. No parser test evidence is claimed.

## Diagrams

```mermaid
flowchart LR
    Command["Command"] --> PackA["Pack A"]
    PackA --> PackB["Pack B"]
    PackB --> Options["Options"]
```

Each parse owns its bound values.
