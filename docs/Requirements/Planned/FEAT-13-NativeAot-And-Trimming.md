# FEAT-13: NativeAOT and Trimming

## Status
**PLANNED**

## Capability
Make supported parser paths usable under trimming and NativeAOT, and verify
real generated and explicit applications by publishing and running them.

## Context
Depends on [FEAT-04](FEAT-04-Typed-Conversion.md),
[FEAT-09](../FEAT-09-Invocation-Lifecycle.md) and
[FEAT-11](FEAT-11-Declarative-Api-And-Generation.md). The build-time/runtime
separation is in [ADR-001](../../Architecture/ADR-001-Project-Boundaries.md).
Full integrated acceptance also requires results [FEAT-07](../FEAT-07-Parse-Results-And-Diagnostics.md),
help [FEAT-08](../FEAT-08-Help-And-Usage.md), completion [FEAT-10](FEAT-10-Completion.md),
scope [FEAT-12](../FEAT-12-Inherited-And-Global-Options.md) and
packs [FEAT-14](FEAT-14-OptionPacks.md). Gate incremental supported subsets
as they activate, but do not claim the full feature while required scenarios
remain planned.
Evidence: `../maz/Console/Cli/CliCompletionProvider.cs` uses runtime
reflection; this proposal does not import that discovery approach.

## Requirements
### FEAT-13-001 - Runtime and generator boundary
Keep generation in the build-time analyzer host and make runtime model and binding independent of generator assemblies.
**Acceptance:** A generated consumer compiles with the generator during build and runs without loading it at runtime; an explicit-model consumer works without a generator.
**Security:** Do not discover arbitrary assemblies or dynamically load application code to find commands.

### FEAT-13-002 - Trimming contract
Document and implement supported conversion, registration and binding paths without unannotated reflection or dynamic-code assumptions that break trimming.
**Acceptance:** Representative generated and explicit applications publish trimmed with relevant warnings treated as errors and preserve parsing behavior when run.
**Security:** Missing trimmed members or unsupported converters fail explicitly rather than bypassing validation.

### FEAT-13-003 - Native publication and execution
For each declared supported runtime/architecture, publish and execute representative generated and explicit parser applications using NativeAOT.
**Acceptance:** A published executable parses a valid nested command and rejects invalid input on both paths; a missing native toolchain is recorded as unavailable, not passed.
**Security:** A failed publish or execution must block the corresponding support claim; preserve actual output and artifact identity as evidence.

### FEAT-13-004 - Support boundaries
Publish the tested target/runtime matrix and limitations, including application-provided converter responsibilities.
**Acceptance:** An untested runtime is marked unverified; an unsupported dynamic feature is reported as unsupported rather than claimed compatible.
**Security:** Do not mask trimming warnings or substitute a managed run for native verification.

## Design
#### Proposed compatibility boundary

The runtime targets `net11.0`; its generator is a build-time Roslyn analyzer
component, not a runtime dependency. The explicit model path must work without
adding/loading the generator at all. Generated registration references known
types, constructors and callbacks directly. Runtime code must not discover
commands/packs/providers through assembly scans, private-field reflection,
Activator, Reflection.Emit or compiled expression trees.

Built-in scalar/enum conversion must remain available when unused framework
metadata is trimmed. Enum token maps can be generated or explicitly supplied;
do not assume runtime reflection enumeration is safe just because ordinary
JIT tests pass. Any allowed reflection must be documented, annotated and
validated against actual trimming/AOT output; no such exception is approved
by this baseline.
The generator-free consumer supplies typed enum token/value maps explicitly;
test enum, nullable enum and nullable numeric conversion with no generator
reference or reflection-discovered metadata.

Custom converter/provider/default delegates are statically rooted by their
registrations. Application callbacks must also obey trimming constraints;
the library cannot certify arbitrary callback bodies. Unsupported dynamic
behavior produces an explicit compile/analyzer/publish failure or documented
unsupported registration, not a fallback to silent defaults. Do not add broad
`NoWarn`, RequiresDynamicCode escapes or linker roots to conceal a warning
without an owner and narrow tested rationale.

#### Representative consumer matrix

| Consumer / environment | Current status | Future acceptance obligation |
| --- | --- | --- |
| Todo.Cli and FileTool.Cli scaffolds, Linux ARM64, SDK 11.0.100/GCC | Scaffold publish/run has been observed; no parser exists. | Replace placeholder-only execution with real generated parsing as requirements activate. |
| Generated parser consumer, Linux ARM64 | Planned, unverified. | Nested command/alias, built-ins and custom converter, pack composition, validation/error paths, handler cancellation and safe help/completion. |
| Explicit-model parser consumer with no generator reference, Linux ARM64 | Planned, unverified. | Equivalent semantic fixtures and failure outcomes to generated consumer. |
| Other RIDs / native compilers | Unverified; not an implicit support promise. | Approve a RID/compiler/native-prerequisite row, then publish and execute on that architecture before claiming support. |

Linux ARM64 is the proposed first product-verification target because its
toolchain exists here, not a universal platform restriction. Expanding the
product support matrix requires approval and evidence; cross-publishing
without executing is not sufficient. Record the .NET runtime/SDK, RID, OS,
compiler/linker, dependency lock identity, git/source snapshot and native
artifact hash with each run.

#### Gate procedure and assertions

Build the whole solution in Release with strict analyzers and locked
dependencies. Deliberately declare the approved RID(s) on representative
consumer projects and regenerate/review their lock files when adding a RID;
then perform locked restore including those RID assets. New RIDs fail
locked restore until this intentional update. Do not promise RID restore
without any lock change or rewrite unrelated library/generator locks.
Native publication can use previously built project references.
Publish each representative application with NativeAOT and trimming/AOT
warnings as errors; then execute the native binary, not its managed DLL.
Also perform a distinct managed trimmed publish with PublishAot=false,
PublishTrimmed=true, TrimMode=link, SelfContained=true, the approved RID,
EnableTrimAnalyzer=true, ILLinkTreatWarningsAsErrors=true and warnings as
errors. Retain evidence that ILLink executed, then run that published output;
an ordinary managed build/run is not a trimmed gate. Use locked restored
assets and --no-restore publish for both paths. Keep command inputs,
stdout/stderr and exit/outcome evidence.

The scenario application must actually access parsed values and invoke
generated binding so the trimmer cannot erase the parser as unused.
Include a valid alias path with nondefault values, invalid integer/unknown
option failures, a custom statically referenced converter, nested OptionPacks,
inherited options, no-callback help/completion and two sequential parses
with different values. Compare observable semantic assertions with JIT tests;
the internal layout or exact binary size is not a correctness assertion.
Use a distinct explicit-model project with no analyzer/generator ProjectReference
or package; inspect dependency graphs and published artifacts for absence
of generator assemblies. The generated consumer must register via actual
emitted descriptors, with construction/handler/custom converters referenced
only by those generated static factories. Neither consumer may satisfy the
gate through hand-written duplicate registration of the other path.

Missing compiler/headers/runtime packs or inability to execute the target is
Unavailable evidence and blocks that row's support claim. Do not substitute
a managed run, a generated-code text snapshot or zero-warning empty assembly
for a real parser native run. Performance and binary-size budgets need
separate measured baselines; no number is invented here.

#### Source extraction and pending choices

Maz shows generated registration can remove much runtime discovery, but its
completion and custom Parse paths still use reflection. Those implementation
details are intentionally excluded; this document specifies the replacement
boundary and test obligations without requiring source access.
Approve the first product RID matrix and any narrow reflection exceptions;
baseline has none. Public annotation/package design remains subject to API
review and the generator host must not be confused with the product target.

## Metrics Impact
**Instruments:** none - planned compatibility validation adds no runtime metrics.

## Verification
Future CI must retain publish/run outcomes for generated and explicit paths,
including invalid input, trim warnings and unavailable environments. No
parser AOT evidence exists yet.

## Diagrams

```mermaid
flowchart LR
    Generator["Generator on build host"] --> Source["Generated source"]
    Source --> Executable["Trimmed NativeAOT runtime executable"]
```
