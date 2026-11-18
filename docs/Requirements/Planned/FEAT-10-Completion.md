# FEAT-10: Completion

## Status
**PLANNED**

## Capability
Offer shell-independent command, option and value suggestions with
asynchronous contextual providers.

## Context
Depends on [FEAT-01](FEAT-01-Command-Model.md),
[FEAT-02](FEAT-02-Options-And-Arguments.md),
[FEAT-03](FEAT-03-Token-Grammar.md),
[FEAT-07](../FEAT-07-Parse-Results-And-Diagnostics.md) and
[FEAT-08](FEAT-08-Help-And-Usage.md). Pack access is owned by
[FEAT-14](FEAT-14-OptionPacks.md). Evidence:
`../maz/Console/Cli/CliCompletionProvider.cs` and
`../maz/CliGenerator.Tests/CompletionTreeGeneratorTests.cs`.

## Requirements
### FEAT-10-001 - Contextual candidates
Provide candidates appropriate to the command path, option position and value being completed, using model metadata.
**Acceptance:** A partial child name suggests matching visible children; after a value-taking option its value candidates appear rather than unrelated command names.
**Security:** Hidden items and sensitive prior values must not appear in default candidates.

### FEAT-10-002 - Asynchronous providers
Allow registered asynchronous value providers to receive bounded partial context and cancellation.
**Acceptance:** A provider sees already parsed non-sensitive context and returns values; cancellation reaches an in-flight provider and no successful completion is reported for a cancelled operation.
**Security:** Providers are application-owned; no implicit network or credential access is performed by the core.

### FEAT-10-003 - Failures and bounds
Return a distinguishable provider failure and constrain candidate count and work, rather than converting errors into empty success.
**Acceptance:** A throwing provider produces a failure outcome; a provider exceeding the configured bound cannot produce an unbounded response.
**Security:** Do not expose provider exception messages, secrets or raw untrusted context by default.

### FEAT-10-004 - Shell boundary
Keep core candidates separate from shell-specific quoting and adapter behavior, with an explicit supported-adapter matrix.
**Acceptance:** Core returns a candidate containing spaces as one logical value; an enabled adapter escapes it according to that shell, and an unsupported shell is reported as unsupported.
**Security:** Adapters must not evaluate candidate text as shell code.

## Design
#### Proposed shell-independent request

The core receives already-decoded logical tokens, a cursor token index,
UTF-16 character offset within that token, the validated model and cancellation.
Token index may equal token count for an empty token appended at the cursor;
its offset must then be zero. Otherwise require an in-range offset.
Tokens/characters after the cursor do not select a command or bind context.
The host adapter, not the core, handles executable-name removal, shell
lexing/quotes and translating cursor positions from bytes/code points.

Use the prefix before the cursor for filtering. Return a replacement range
covering the whole active token/value segment, including any suffix after
the cursor, so completing `--mode=fa|st` replaces the value, not the option
name or only `fa`. The `|` in examples denotes the cursor, never input text.
For `--name=value`, the value segment begins after the first `=`.
An append position returns a zero-length replacement at a new token.
Determine the active segment before choosing a provider. At/before the first
equals, complete only the option-name segment `[0, equalsIndex)`, preserving
the entire existing `=value` suffix; after equals, replace only the value
segment. This also applies to short equals (`-m=fast`). Unknown or non-valued/
negating aliases after equals produce no value candidates and no provider
call. Null vectors/elements and invalid cursor coordinates are API argument
errors, not successful empty completion.

Proposed candidate fields: insertion text (one logical value), safe display
text, optional description, kind (command/option/value/positional), definition
identity and replacement coordinates. Results distinguish Completed,
Cancelled, ContextFailed, ProviderFailed and LimitReached. An empty Completed result means
no matches, never a swallowed exception.

#### Context resolution and providers

Walk only completed tokens through FEAT-03's position-aware grammar, tolerating
incomplete required values/positionals. Do not run normal requiredness,
default factories, application validators, command constructors or handlers.
Invalid completed structure yields ContextFailed with FEAT-07 coordinates, not
invocation. The active incomplete token is not itself an unknown-option error.

| Prior syntax state | Active-token completion |
| --- | --- |
| RequiredScalar or RepeatedSingleCollection awaiting its one value | Bare prefixes and valid numeric-negative prefixes target that definition's values. Another dash prefix offers option names instead and records the pending MissingValue, never invents a consumed value. |
| BooleanFlag, no inline value | Offer true/false plus normal command/positional routing for other bare prefixes. Exact true/false route only as boolean values, never a competing child candidate. A dash prefix offers options. |
| BooleanFlag after equals | Only true/false values; no application converter/provider. |
| Negating alias | No value state; next token resumes normal routing, and equals provides no value candidates. |
| OptionalScalar without equals | Immediately resume normal routing; values complete only inside its equals segment. |
| GreedyCollection after space | Value state persists across bare elements until a dash/marker boundary; numeric negatives remain values only for FEAT-03 numeric targets. Other dash prefixes offer options. |
| Any ordinary valued mode after equals | Complete exactly its inline value segment; greedy inline equals supplies just one value and does not swallow the following word. |

- While command selection remains open, suggest visible child names/aliases
  matching the prefix. Option-like prefixes suggest currently visible option
  names. After the first positional, do not offer child-command routing.
- After `--`, never offer commands/options; offer only the active positional's
  values. A rest positional continues to own subsequent positional slots.
- Static value lists and enum names come from declared conversion metadata,
  not `ToString()` over arbitrary objects. Use FEAT-01 name comparison for
  option/command filtering, and the registered value provider's documented
  value comparison policy.

Provider lookup is keyed by `(model, selected canonical path, declaring owner
inclusion path, definition identity)`, not a global
alias string. Siblings may both use `--name` with different providers.
Applications explicitly register typed delegates/factories; no Activator,
reflection field walk, global mutable registry or implicit credential/network
lookup is permitted. A provider receives the prefix, selected path and
read-only successfully converted prior non-sensitive values from its explicit
dependency-handle list (empty by default), never arbitrary values in scope.
Reject unknown/ambiguous targets/dependencies and hidden/sensitive or
cross-branch dependency registrations at model validation; scope visibility
must hold everywhere the provider applies. A currently missing, duplicate
or conversion-invalid dependency stays unavailable, not a guessed value.
Built-in conversion is permitted; custom converters must
explicitly opt in as completion-safe. No absent factory is called to invent
context. Missing/invalid/redacted context is unavailable, not a default value.

Pack lookup uses FEAT-14 inclusion identity and safe data views, not arbitrary
application helper invocation or a first-match search of the root tree.
Hidden/sensitive targets are excluded from all core value suggestions and
provider invocation, including explicitly registered providers;
no raw prior argv or secret strings are automatically logged.

#### Bounds, failures and adapter matrix

Proposed bounds: FEAT-07 input/model bounds; inspect at most 512 returned
candidates, emit at most 128, and accept at most 1024 UTF-16 code units per
candidate. Candidate duplication uses exact ordinal insertion text and
replacement range; retain first occurrence. Preserve declared/provider order
rather than culture-sensitive resorting. Exceeding a bound returns LimitReached
with explicit truncation state, never unmarked complete success.
Count all raw yielded candidates before prefix filtering/dedup, across the
request, so duplicate/nonmatching/infinite sources cannot evade the bound.
Processing order is: inspect/count; validate candidate total insertion/
display/description length (at most 1024 UTF-16 units combined); apply
visibility/privacy and sanitize display per FEAT-08; prefix-filter; dedup;
append up to 128 distinct matches. Oversized candidates yield LimitReached,
not silent skipping. At raw item 512, allow at most one MoveNext lookahead
solely to detect exhaustion; a 513th item or 129th distinct match proves
LimitReached. Never inspect unbounded extra items to prove completeness.
Return any partial candidates explicitly marked incomplete on failure;
they are not a successful complete set or an automatically selected value.

Pass cancellation through awaiting and asynchronous enumeration, dispose
enumerators, and surface provider faults without exception text in default
presentation. Work/count limits constrain core code, not arbitrary callback
CPU or side effects: noncooperative providers cannot be safely killed. Do
not advertise a hard provider time limit without an explicit cooperative
deadline/host-isolation design. No fallback from ProviderFailed to empty
Completed is allowed.
Pre-cancellation runs no providers. On normal/exhaustion/limit paths, dispose
the enumerator and check caller cancellation immediately before finalizing:
requested cancellation yields Cancelled instead of Completed/LimitReached.
Once finalized, later cancellation cannot alter the immutable result.
A provider/enumerator fault yields ProviderFailed even if caller cancellation
arrives later; an OCE with requested caller cancellation yields Cancelled,
while a foreign OCE without caller cancellation is ProviderFailed.
Preserve both enumeration and cleanup faults if both occur, without default
exception-text rendering. Await/dispose fully before reporting an outcome.

| Boundary | Initial proposed support |
| --- | --- |
| Core logical-token completion | Required by this feature. |
| Explicit static/asynchronous providers and typed safe context | Required by this feature. |
| Bash, Zsh, Fish, PowerShell adapters | Not yet selected; unsupported until per-shell lexing/escaping tests and protocol are specified. |
| Raw command-line tokenization / shell execution | Not part of the core API. |

Acceptance fixtures include `fi|`, `file --co|`, `file --mode=fa|st`,
`file --mode |`, `file --filter |` (optional: not a filter value),
`file -- -na|`, a value containing a literal space, sibling provider reuse,
two same-type packs, sensitive dependencies, cancelled enumeration, a provider
throwing after its first result, and a 513-candidate response. A partial failure
must remain a failure even if earlier candidates exist.
Add `--mo|de=fast`, `--mode|=fast`, `--mode=|fast`, `-m|=fast`,
`--unknown=x|`, negating equals, zero providers on hidden/sensitive targets,
513 duplicate/nonmatching results, an oversized display field, provider
dependency leakage, and cancelled-vs-finalized-result race fixtures.

#### Extraction and pending approval

Maz combines a generated static tree, alias-keyed registries, simplified
double-quote tokenization and reflection-based pack search. Some paths swallow
provider faults as no output. This proposal keeps static/dynamic metadata but
replaces those boundaries with typed scope, logical-token cursor ranges and
explicit failure. Approve cursor representation, converter opt-in, limits and
future shell choices before implementation; no maz reading is needed.

## Metrics Impact
**Instruments:** none - planned completion introduces no runtime metrics.

## Verification
Future tests cover incomplete paths, value context, cancellation, provider
exceptions, candidate limits, hidden metadata and shell escaping per approved
adapter. No shell support or test result is claimed today.

## Diagrams

```mermaid
flowchart LR
    Argv["Partial argv"] --> Candidates["Core candidates"]
    Candidates --> Adapter["Optional shell adapter"]
```
