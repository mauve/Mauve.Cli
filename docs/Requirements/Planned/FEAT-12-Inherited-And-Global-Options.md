# FEAT-12: Inherited and Global Options

## Status
**PLANNED**

## Capability
Expose explicitly scoped parent or global options to descendant commands,
with deterministic visibility and conflict handling.

## Context
Depends on [FEAT-01](FEAT-01-Command-Model.md),
[FEAT-02](FEAT-02-Options-And-Arguments.md) and
[FEAT-03](FEAT-03-Token-Grammar.md). Evidence:
`../maz/Console/Cli/CommandDef.cs`,
`../maz/Console/Cli/Shared/GlobalBehaviorOptionPack.cs`, and
`../maz/specs/CLI Parser Specification.md`. Pack composition is separate:
[FEAT-14](FEAT-14-OptionPacks.md).

## Requirements
### FEAT-12-001 - Explicit scope
An option must become visible to descendants only through an explicit inherited/global scope declaration.
**Acceptance:** An inherited root `--verbose` is visible to a nested leaf; an ordinary root-local option is not visible there.
**Security:** Reusing a pack or declaring a parent option must not implicitly grant global visibility.

### FEAT-12-002 - Placement and binding
Bind visible inherited options consistently at approved positions along a selected command path.
**Acceptance:** Under the proposal, `--verbose file copy` and `file copy --verbose` bind the same inherited option; an option local to another branch is rejected.
**Security:** Never bind an option from an unrelated command or silently reinterpret its value as a subcommand.

### FEAT-12-003 - Scope conflicts
Validate collisions between all simultaneously visible names and aliases before parsing.
**Acceptance:** A child alias colliding with an inherited parent alias fails regardless of declaration order; reuse on disjoint sibling branches remains valid.
**Security:** Reject shadowing rather than allowing a descendant to intercept a security-relevant ancestor option.

### FEAT-12-004 - Descendant consistency
Expose the same visible definition and value provenance to downstream help, result and invocation consumers.
**Acceptance:** A leaf's help and parse result identify the inherited option once; a nested descendant receives the same explicitly supplied value.
**Security:** Visibility must not result in duplicate binding, state leakage or unintended disclosure of hidden metadata.

## Design
#### Proposed scope algorithm

Every option has an owning command and explicit scope `Local` or `Inherited`.
Global is shorthand for an inherited option owned by root, not a separate
registry. Pack membership never changes scope. A pack's individual definitions
may be inherited only when explicitly declared as such.

For a selected command, effective definitions are its local options/flattened
packs in their declared order, followed by inherited ancestors nearest first,
preserving each ancestor's own order. A definition appears once per inclusion
identity. Build a name/alias map over that set and reject every simultaneous
collision under FEAT-01 comparison, including two aliases of one definition.
Do not silently skip a parent's definition because a child used its name.
This order controls default/validator ordering and supplies help's grouping
input, not conflict precedence or a strict global rendered sequence (FEAT-08).
Requiredness, absent factories and validators apply once to this final set;
there is no separate per-ancestor validation/default pass.

Validate effective sets for every reachable command before parsing so an
unvisited ambiguous branch cannot become a latent fallback path. Ordinary
ancestor-local definitions are excluded from descendant sets; reusing their
names on a child is valid when the sets do not overlap. Unrelated sibling
names/providers/defaults never enter the selected branch's scope.

#### Position rules

An inherited ancestor option may occur before, between or after descendant
command tokens, provided its declaring ancestor has already been selected.
A descendant-local option may occur only after selecting that descendant;
there is no lookahead that preloads a leaf's aliases at root.
Before descending, an ancestor-local option can be recognized temporarily,
but if the final command is a descendant that occurrence becomes
OptionOutOfScope. Never rebind it to a same-spelled child option.
This final recheck makes parent-local visibility unambiguous without
guessing the future path.
Arity-scan excluded ancestor-local occurrences using their original definitions,
but never convert/bind/default/validate them or count their scalar duplicates.
Emit one OptionOutOfScope per excluded occurrence, retaining any arity error
on that same occurrence; these are structural failures that block help and
all later conversion callbacks. Retain the original definition/index, not
the child's same-spelled identity.

Static OwnerDefinitionId and OwnerInclusionId use the declaring command and
pack path, never the selected leaf. An operation's view identity is
`(model identity, selected canonical path, OwnerInclusionId)`. Inheriting a
pack member exposes its declaring owner's handle on the descendant without
copying/re-owning it. Other local-only members of that same pack are
unavailable through the descendant's restricted view; missing optional
members also retain their normal FEAT-06 unavailable state.
Sensitive explicit values remain available to deliberate typed host access
on Validated results; this is not blanket application-value redaction.
They must not enter default help/diagnostic/completion projections.

After the first positional, command selection is closed, but visible options
can still occur until `--`. After `--`, no inherited/local option is recognized.
Only the selected command's positional definitions bind input; there is no
ancestor positional inheritance or parent-before-child argument pipeline.

#### Worked fixture

Root `tool` has inherited `--verbose`/`-v` and local `--root-only`; child `file`
has inherited `--region`; leaf `copy` has local `--output`/`-o`.
Sibling `list` may have its own local `--output` and a different provider.

| argv / definition | Expected result |
| --- | --- |
| `["-v","file","copy"]` / `["file","copy","-v"]` | Same inherited boolean definition/value/provenance. |
| `["file","--region=west","copy"]` | Valid: declaring ancestor `file` selected before the occurrence. |
| `["--region=west","file","copy"]` | UnknownOption at root; no future-option lookup. |
| `["-o=x","file","copy"]` | UnknownOption, not leaf output. |
| `["--root-only=x","file","copy"]` | OptionOutOfScope at original root-only token. |
| Two root-only occurrences before descending | Two OptionOutOfScope diagnostics; no DuplicateOption or conversion/default/validator calls for those excluded definitions. |
| `["file","copy","--root-only=x"]` | UnknownOption at leaf. |
| Root-local and leaf-local both named `--format` | Model valid; only owner-position/final-scope usage is valid. |
| Root inherited `--verbose`, leaf alias `--verbose` | Invalid model, regardless of declaration order or hidden metadata. |
| Two disjoint leaves both use `-o` | Valid; distinct scoped identities, no global collision. |
| `["file","copy","--","-v"]` | Literal positional `-v`, not inherited verbose. |

#### Extraction and approval

Maz walks ancestors from a leaf, adds recursive options and skips duplicate
canonical names. That can conceal alias/shadowing ambiguity. The proposal
uses the same explicit inheritance use case but rejects collisions instead,
with no shared mutable option result or implicit pack-global behavior.
Approve final-scope rechecking, ordering and strict conflict policy before
implementation. FEAT-03/07/08 own token processing, error representation and
help rendering; this scope contract is complete without maz.

## Metrics Impact
**Instruments:** none - no scope metrics are added for planned work.

## Verification
Future tests cover nested placement, local and sibling visibility,
name/alias collisions, case boundaries and help/result integration.
No tests have established these semantics.

## Diagrams

```mermaid
flowchart LR
    Root["Root with inherited option"] --> Child["Child"]
    Child --> Leaf["Leaf with same visible option"]
```
