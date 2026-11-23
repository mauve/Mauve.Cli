# FEAT-06: Defaults and Validation

## Status
**ACTIVE**

## Capability
Distinguish supplied values from absence, apply declared defaults, and
validate individual and related values before invocation.

## Context
Depends on [FEAT-02](FEAT-02-Options-And-Arguments.md),
[FEAT-04](FEAT-04-Typed-Conversion.md),
[FEAT-05](FEAT-05-Cardinality-And-Special-Forms.md),
[FEAT-07](FEAT-07-Parse-Results-And-Diagnostics.md), and
[FEAT-16](FEAT-16-Environment-Option-Binding.md). Evidence:
`../maz/CliGenerator.Tests/CliParserBehaviorTests.cs` and
`../maz/specs/CLI Parser Specification.md`. Environment values are a separate
higher-precedence input source; this feature continues to own declared
defaults and callback ordering.

## Requirements
### FEAT-06-001 - Presence and requiredness
Track explicit input occurrence independently of the resulting value and validate requiredness against a documented presence policy.
**Acceptance:** Explicit `--count=0` is supplied even if zero is the default; a valid environment-bound required option satisfies presence, while an option absent from both argv and environment fails.
**Security:** A default must not silently satisfy an explicit-input requirement.

### FEAT-06-002 - Defaults and factories
Apply declared defaults only when the policy calls for them, once per parse, without overwriting explicit values.
**Acceptance:** An absent option receives its declared factory result; explicit default-valued input remains marked supplied; separate parses do not share mutable factory results.
**Security:** Factory failure fails the parse without handler invocation or exposing sensitive exception details.

### FEAT-06-003 - Value validation
Apply registered per-value validators after successful conversion and report failure without substituting a default.
**Acceptance:** A range validator accepts its bounds, rejects an out-of-range value, and does not run on a failed conversion.
**Security:** Validation failures do not expose raw secrets or yield usable partial success.

### FEAT-06-004 - Cross-value validation
Validate declared relationships after values/defaults are assembled and before invocation.
**Acceptance:** A mutually exclusive pair fails when both occur and succeeds when only one occurs; an invalid combination never reaches a handler.
**Security:** A validator exception or unverifiable prerequisite fails closed, not as success.

## Design
#### Value states and defaults

Accepted baseline: track occurrence (`Absent`, `Bare`, `WithValue`) separately
from origin (`Absent`, `Default`, `Explicit`) and effective-value availability.
Zero, false, empty string and null are values, not evidence of absence.
A successful bare occurrence has explicit origin even when a bare factory
produces the same value as an absent default.

Each definition may declare one absent default, either a typed constant or
a factory, and an independent bare-value policy for optional-value syntax.
Reject multiple absent defaults; do not give a factory silent precedence
over a constant. Do not infer defaults from runtime type alone. Standard
boolean-flag and collection helpers may lower to an explicit false constant
or empty-list factory respectively; those defaults must be visible in the
validated schema. Nullable optional declarations may lower an explicitly
chosen absent/bare-null policy; nullability alone does not lower token arity.

Immutable built-in constants are shareable; mutable reference/collection
defaults must use per-parse factories. A factory is invoked at most once
per relevant definition per parse and only if needed. Repeated reads do
not reevaluate it. Application factories/converters own deeper custom
object graphs: the library cannot deep-clone arbitrary application objects.
Requiredness means explicit occurrence, even if an absent default exists.
An absent default on a required definition is inert: never evaluated,
passed to value validators or shown as an effective help default, and never used to satisfy
generated binding safety. It may be retained as metadata for a future
explicitly changed definition; requiredness still wins.
This does not disable a separate bare policy on a present required occurrence.
An optional definition with no default can remain unavailable; consumers
must use a presence-aware accessor. Generated nonnullable bindings require
either required successful input or a declared nonnull default (FEAT-11).
Every possible successful bare policy must also satisfy AllowsNull.
Null constants on a nonnullable definition fail model validation; an
unexpected null factory/bare result yields ValidationFailed before binding.

#### Processing order and failures

1. Scan tokens and resolve the final command, checking syntax/scope first.
   If structural errors exist, return Invalid without value converters or
   later callbacks. Otherwise resolve FEAT-08's built-in help-token grammar;
   a true, non-conflicting help or help-tree occurrence returns
   HelpRequested or HelpTreeRequested now, before all conversion,
   requiredness and defaults.
2. Convert supplied values in token order. Keep failed definitions invalid;
   never replace invalid supplied input with an absent default.
3. Diagnose missing required occurrences and positional minimum arity.
   Do not evaluate a default merely to repair missing required input.
4. Produce present bare values regardless of requiredness, and absent defaults
   only for eligible optional definitions, once, in effective-definition order.
5. Run per-value validators on available, successfully assembled values in
   effective-definition order and validator-registration order. Validate
   defaults too. Missing optional values have no value validator call.
6. Only when syntax, conversion, requiredness and all value validation have
   succeeded, run cross-value validators in registration order.
7. Produce a successful result only if all applicable stages succeed.
   Invocation is a distinct FEAT-09 operation.

Structural failures suppress conversion and all later callback stages.
After a structurally valid scan, returned conversion errors and missing
required inputs may be accumulated, but together gate entry into the default/
value-validation stage. Within value-validation, run every applicable
registered validator in order up to the diagnostic cap even after a returned
failure; run cross-validation only if that whole stage succeeded.
Within cross-validation likewise accumulate returned failures up to the cap.
This prevents needless callbacks on malformed input without turning one
validator failure into an unspecified fail-fast policy.
A returned validation failure is an input diagnostic. A thrown factory or
validator exception is an application callback fault: abort and propagate
to the host without handler invocation or automatic message logging.
No parse result is produced on a propagated fault; prior accumulated
diagnostics are not returned separately. The original exception may contain
application-sensitive details for the authorized host; the library adds no
message/presentation or logging of its own.

Validation context is a read-only view of the current parse, not a service
locator or shared command instance. Relationship checks must say whether
they inspect explicit presence or effective values; a mutually exclusive
input pair normally tests presence rather than nonnull/true values.
Help and completion bypass callbacks according to FEAT-08/10.
Validator inputs expose read-only container views, not mutable list builders.
Arbitrary custom reference values cannot be deep-frozen; validators must
not mutate them, and callers wanting enforced immutability must register
immutable value types. Add tests that ordinary collection validators cannot
mutate the successful result container.

#### Acceptance matrix

| Definition / input | Expected state or callback behavior |
| --- | --- |
| Count default 0, no occurrence | Origin Default; WasProvided false; effective value 0. |
| Same definition, `--count=0` | Origin Explicit; WasProvided true; no factory called. |
| Nullable optional filter, bare `--filter` with bare-null policy | Bare occurrence, Explicit origin, available null; distinct from absence. |
| Required count with default 3, no occurrence | Missing-required error; default does not authorize dispatch or run its factory. |
| Required count supplied by a valid environment value | Presence is satisfied and the value retains Environment origin; an absent default factory does not run. |
| Required OptionalScalar supplied bare with a nonnull bare factory | Presence is satisfied and the bare factory runs once; requiredness suppresses only its absent default, not bare behavior. |
| Integer `--count=bad` with default 3 | InvalidValue; no default substitution or validators. |
| Fresh-list default in two parses | Different list instances; modifying one result does not change the other. |
| Default 20 with maximum validator 10 | ValidationFailed, even though no value was typed. |
| `--left=false --right=false` with presence-based exclusivity | Invalid combination: both were explicitly supplied. |
| First cross-validator fails | Later cross-validators still run in order unless the diagnostic cap is reached; no invocation. |

#### Source extraction and decisions

Maz's defaults favor a factory over a constant, infer optionality from
nullability/initializers and retain values on reusable option objects.
This plan instead separates absent/bare behavior, rejects competing defaults
and allocates parse-local state. Environment/configuration lookup is not
performed by defaults or factories; the invocation-only environment source
is owned by [FEAT-16](FEAT-16-Environment-Option-Binding.md).
Presence, default and callback-order policy follow this accepted contract;
the complete behavior is captured here, not delegated to maz.

## Metrics Impact
**Instruments:** none - validation adds no runtime instrumentation.
**Diagnostics:** `ValidationFailed`

## Verification
Portable behavior tests are declared in
[`CliParserTests`](../../tests/Mauve.Cli.Tests/Parsing/CliParserTests.cs)
for presence, required defaults, factory isolation, value validation,
cross-value validation and callback faults. No product test-run evidence has
been recorded yet; see the [validation ledger](../validation-gaps.md).

## Diagrams

```mermaid
flowchart LR
    Presence["Presence"] --> Defaults["Defaults"]
    Defaults --> ValueValidation["Value validation"]
    ValueValidation --> CrossValidation["Cross-value validation"]
```
