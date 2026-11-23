# FEAT-16: Environment Option Binding

## Status
**ACTIVE**

## Capability
Bind eligible options from environment variables during invocation while
preserving explicit argv parsing, typed conversion, validation and provenance.

## Context
Depends on [FEAT-02](FEAT-02-Options-And-Arguments.md),
[FEAT-04](FEAT-04-Typed-Conversion.md),
[FEAT-06](FEAT-06-Defaults-And-Validation.md),
[FEAT-07](FEAT-07-Parse-Results-And-Diagnostics.md),
[FEAT-09](FEAT-09-Invocation-Lifecycle.md), and
[FEAT-11](FEAT-11-Declarative-Api-And-Generation.md). The design is recorded
in [ADR-016](../Architecture/ADR-016-Environment-Option-Binding.md).

## Requirements
### FEAT-16-001 - Host configuration and names
Environment binding is opt-in through `CliHostOptions`. Options inherit the
host eligibility default (enabled by default) and can explicitly enable or
disable binding. A full environment-variable name override takes precedence
over generated naming. Generated names use the configured prefix, or the
normalized root command name when omitted, followed by `_` and the normalized
long option name. Normalize by replacing hyphens with underscores and
uppercasing invariantly. Short-only options without an override are not
bindable. Environment lookup is injectable; the default reads the process
environment.
**Acceptance:** Host binding is off by default; configured prefix `MY_APP`
and option `--log-level` resolve `MY_APP_LOG_LEVEL`; omitted prefix on root
`my-tool` resolves `MY_TOOL_LOG_LEVEL`; an explicit full name is used as
written; short-only options without override are skipped; option policy and
host default compose as documented. When host environment processing is
enabled, default help prints each eligible environment-variable name inline
with its option; names are omitted when processing is disabled.
**Security:** Do not read environment values during direct parsing,
completion, help, or structurally invalid input. Do not expose environment
names or values in diagnostics; help may display eligible variable names
inline when processing is enabled, but never their values.

### FEAT-16-002 - Precedence and provenance
Apply declared absent defaults when no input is supplied, use an environment
value in preference to that default, and let an explicit command-line option
override both. A valid environment value satisfies required option presence.
Record environment origin distinctly from explicit argv and defaults.
**Acceptance:** A default of `1`, environment value `2`, and argv value `3`
resolve respectively to 1, 2, and 3 as higher-precedence sources are added;
environment-only required input succeeds with `ValueOrigin.Environment`,
`ValuePresence.WithValue`, and no argv token indices; explicit collection
input replaces rather than appends to the environment collection, and the
environment lookup is not called for an explicitly supplied option.
**Security:** Never convert invalid environment input into a default or
dispatch a handler after invalid input.

### FEAT-16-003 - Conversion and validation
Convert environment values with the selected option converter and numeric
culture, then run ordinary value and cross-value validation. Scalar and
optional scalar inputs use one complete variable value. Boolean flags accept
only `true` or `false`, counting options accept a converted integer, and
collection values use strict CSV with a configurable single-character
delimiter, doubled-quote escaping, and preserved empty fields. Environment
values and collection field counts obey the model's existing token-length and
argument-count limits.
**Acceptance:** Built-in and registered converters apply to scalar and
collection elements; CSV handles quoted delimiters and escaped quotes,
preserves empty entries, and rejects malformed quoting; oversized values and
collections fail safely; conversion and CSV diagnostics identify the option
but contain no argv coordinates or raw environment values; conversion and
validator faults follow existing fail-closed callback behavior.
**Security:** Environment conversion failures remain safe structured input
errors, while thrown application lookup/converter/validator faults propagate
without being rendered or logged by the library.

### FEAT-16-004 - Model parity and AOT
Explicit definitions and generated command/OptionPack declarations expose
equivalent environment-binding metadata and use the shared invocation path.
Environment lookup is invocation-local and does not add runtime reflection
or generator dependencies.
**Acceptance:** Generated and explicit definitions with equivalent metadata
bind the same environment values, and trimmed and NativeAOT acceptance
exercises environment binding through an injected lookup.
**Security:** Do not discover option metadata through runtime assembly scans;
completion does not read environment variables or execute lookup callbacks.

## Design
Environment binding is an invocation input source, not argv substitution.
`CliParser.Parse` remains a pure parse of the provided argument vector.
`CliInvoker` supplies host configuration to parsing, which resolves eligible
values after structural and help checks and before requiredness, conversion,
defaults and validation. Environment values do not receive invented token
indices. The host resolves only options absent from argv; environment
collections replace rather than merge with argv collections.

An option uses `EnvironmentVariableBinding.Inherit`, `Enabled` or `Disabled`.
The inherited host eligibility default is true, but the complete feature is
disabled unless `CliHostOptions.EnableEnvironmentVariables` is true. The
explicit option name is a full name and is not prefixed. Otherwise the root
command name (unless `EnvironmentVariablePrefix` is configured) and the
canonical long option name are normalized by replacing `-` with `_` and
uppercasing invariantly. Short-only options require an explicit name.
Selected eligible options may not resolve to duplicate names.

The host lookup delegate returns null for absence; an empty string is
present input. Each eligible absent option is looked up at most once per
invocation. CSV fields pass through the same typed element converter as
argv-supplied collection values. Explicit argv values suppress lookup of the
environment source entirely. Lookup and application callback exceptions
propagate as application faults.

## Metrics Impact
**Instruments:** none - environment values are invocation inputs and are not
logged or measured.
**Diagnostics:** existing `InvalidValue` and `MissingRequired` diagnostics
cover malformed or absent environment input without adding a raw-value
diagnostic surface.

## Verification
`CliInvokerTests.InvokeAsync_BindsEnvironmentValuesBetweenDefaultsAndArgv`,
`InvokeAsync_ConvertsEnvironmentFlagsCountsAndCsvCollectionsSafely`,
`InvokeAsync_UsesOptionBindingOverridesAndSkipsEnvironmentForHelpOrInvalidOrExplicitOptions`,
and
`InvokeAsync_UsesOptionEligibilityDefaultAndRejectsEnvironmentNameCollisions`
cover naming, eligibility, precedence, requiredness, typed conversion, safe
diagnostics, CSV parsing and lookup isolation.
`InvokeAsync_EnforcesEnvironmentValueAndCollectionLimits` covers the model
input bounds, and `InvokeAsync_PropagatesEnvironmentLookupFaults` covers
lookup fault propagation. `HelpDocumentTests.Run_HelpPrintsEnvironmentNamesInlineOnlyWhenEnabled`
covers inline help metadata, host prefix projection, disabled-host omission,
and lookup isolation.
`CliModelGeneratorTests.Generate_LowersEnvironmentMetadataForCommandsAndOptionPacks`
and
`AotAcceptanceTests.VerifyGeneratedAndExplicitEnvironmentBinding` cover
generated lowering, explicit parity, and AOT-safe invocation with an injected
lookup. Observed outcomes are recorded in the
[validation ledger](../validation-gaps.md).

## Diagrams

```mermaid
flowchart LR
    Default["Declared default"] --> Environment["Environment value"]
    Environment --> Argv["Explicit argv value"]
    Argv --> Convert["Shared conversion and validation"]
```
