# FEAT-13: NativeAOT and Trimming

## Status
**IMPLEMENTED**

## Capability
Make supported parser paths usable under trimming and NativeAOT, and verify
real generated and explicit applications by publishing and running them.

## Context
Depends on [FEAT-04](FEAT-04-Typed-Conversion.md),
[FEAT-09](FEAT-09-Invocation-Lifecycle.md) and
[FEAT-11](FEAT-11-Declarative-Api-And-Generation.md). The build-time/runtime
separation is in [ADR-001](../Architecture/ADR-001-Project-Boundaries.md).
Integrated acceptance also uses results [FEAT-07](FEAT-07-Parse-Results-And-Diagnostics.md),
help [FEAT-08](FEAT-08-Help-And-Usage.md), completion [FEAT-10](FEAT-10-Completion.md),
scope [FEAT-12](FEAT-12-Inherited-And-Global-Options.md) and
packs [FEAT-14](FEAT-14-OptionPacks.md). The verified support matrix currently
contains Linux x64 only; see [ADR-013](../Architecture/ADR-013-NativeAot-Support-Matrix.md).

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
#### Compatibility boundary

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
| Focused Mauve.Cli.Aot.Tests, Linux x64 | The comprehensive generated and explicit matrix passed once through managed MTP and once as a NativeAOT MTP executable. Both TRX reports contained exactly one passing FEAT-13 test and all four requirements. | Repeat with `bash scripts/verify-aot.sh` when changing parser, generator, conversion, help, completion, packs or invocation behavior. |
| Generated Todo.Cli, Linux x64 | Managed trimmed and NativeAOT publishes ran a small check of the normal generated Todo model with warnings treated as errors. | Repeat with `bash scripts/verify-aot.sh` when changing the generator or example model. |
| Explicit FileTool.Cli without a generator reference, Linux x64 | Managed trimmed and NativeAOT publishes ran a small check of the normal explicit copy model with warnings treated as errors. | Repeat with `bash scripts/verify-aot.sh` when changing the runtime or example model. |
| Linux ARM64 and all other RIDs | Unverified. No support claim is made. | Add a native or cross-publish-plus-execution environment and retain its independent evidence before claiming support. |

The host reports Ubuntu 26.04, `linux-x64`, .NET SDK 11.0.100, and Clang
21.1.8. The verifier rejects a non-native `linux-x64` host, records the SDK,
compiler, lockfile/source hashes, complete command output and artifact hashes
under `artifacts/aot-verification/linux-x64/`, and never treats cross-publish
or a managed run as native evidence.

#### Gate procedure and assertions

Build the whole solution in Release with strict analyzers and locked
dependencies. Deliberately declare the approved RID(s) on representative
consumer projects and regenerate/review their lock files when adding a RID;
then perform locked restore including those RID assets. New RIDs fail
locked restore until this intentional update. Do not promise RID restore
without any lock change or rewrite unrelated library/generator locks.
Native publication can use previously built project references.
Publish the focused AOT test project with NativeAOT and trimming/AOT warnings
as errors, execute its native MTP binary, and compare its exact test count and
Governance metadata with a normal managed MTP run. Publish each representative
application with NativeAOT and execute the native binary, not its managed DLL.
Also perform distinct managed trimmed application publishes with PublishAot=false,
PublishTrimmed=true, TrimMode=link, SelfContained=true, the approved RID,
EnableTrimAnalyzer=true, ILLinkTreatWarningsAsErrors=true and warnings as
errors. Retain evidence that ILLink executed, then run that published output;
an ordinary managed build/run is not a trimmed gate. Use locked restored
assets and --no-restore publish for both paths. Keep command inputs,
stdout/stderr and exit/outcome evidence.

The focused AOT test must actually access parsed values and invoke generated
binding so the trimmer cannot erase the parser as unused.
Include a valid alias path with nondefault values, invalid integer/unknown
option failures, a custom statically referenced converter, nested OptionPacks,
inherited options, no-callback help/completion and two sequential parses
with different values. Compare observable semantic assertions with JIT tests;
the internal layout or exact binary size is not a correctness assertion.
The explicit example must have no analyzer/generator ProjectReference or
package; inspect published example artifacts for absence of generator
assemblies. The generated paths must register through actual emitted
descriptors, with construction and handlers referenced by generated static
factories. The focused test may own richer acceptance-only declarations;
examples must contain only their user-facing models and independent smoke
checks.

Missing compiler/headers/runtime packs or inability to execute the target is
Unavailable evidence and blocks that row's support claim. Do not substitute
a managed run, a generated-code text snapshot or zero-warning empty assembly
for a real parser native run. Performance and binary-size budgets need
separate measured baselines; no number is invented here.

#### Verified support boundary

Linux x64 is the first verified product RID. Other architectures remain
unverified until their own native artifacts are executed. Applications own
the trimming/AOT safety of callbacks and custom types they register; the
library does not promise arbitrary application code is trim-safe.

## Metrics Impact
**Instruments:** none - planned compatibility validation adds no runtime metrics.

## Verification

`bash scripts/verify-aot.sh` performs locked restore, strict Release builds,
managed trimmed publish/run and NativeAOT publish/run for both examples. It
also runs the focused AOT test through managed MTP and as a NativeAOT MTP
executable, imports both TRX reports, and requires exact one-test parity. The
focused scenario checks valid nested alias binding, inherited and nested pack
values, custom and enum conversion, nullable values, help/completion without
handler dispatch, invalid input without dispatch, and sequential invocation
isolation.

Observed on Ubuntu 26.04, .NET SDK 11.0.100, Clang 21.1.8, RID `linux-x64`.
Both example modes printed `AOT smoke check passed.` The managed and native
focused test executions each reported one passing test and preserved
FEAT-13-001 through FEAT-13-004. The latest artifact SHA-256 values were:

| Consumer | Trimmed managed DLL | Native executable |
| --- | --- | --- |
| Todo.Cli | `fa6f66a1a2e16a29dfb83014f0ac8dfd8ee4e3e7a774225e5d39e576af14b5ab` | `38c9fb3ebc23ba4c46422dee0a36827e5daf1fe9055062630dca2639ec7db978` |
| FileTool.Cli | `2dd76aad4b2a24684975d1094f836cc48c758b22ecba076bd84849ca91c6dcad` | `e5284c2154716a45df6b121149ef9ebc2378547c3b89759f8491212ce3fb2040` |
| Mauve.Cli.Aot.Tests | managed MTP TRX evidence | `0e560070fb3d8c4e41aa1a8732387d4ac44bafc4d2dfe44f0e4667039ac87569` |

The metadata-bearing behavioral test is
`AotAcceptanceTests.VerifyGeneratedAndExplicitScenarios`. ARM64 and other
RIDs remain unverified.

## Diagrams

```mermaid
flowchart LR
    Generator["Generator on build host"] --> Source["Generated source"]
    Source --> Executable["Trimmed NativeAOT runtime executable"]
```
