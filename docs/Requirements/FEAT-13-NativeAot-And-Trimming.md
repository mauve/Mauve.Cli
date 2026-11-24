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
packs [FEAT-14](FEAT-14-OptionPacks.md), and directives
[FEAT-15](FEAT-15-Directives.md). The verified support matrix currently
contains Linux x64 only; see [ADR-013](../Architecture/ADR-013-NativeAot-Support-Matrix.md).
Multicall invocation and real-symlink acceptance are owned separately by
[FEAT-17](FEAT-17-Multicall-Dispatch.md).

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
| Focused Mauve.Cli.Aot.Tests, Linux x64 | The comprehensive generated and explicit matrix, model-owned converters, ordered unions, nullable custom collections and directive scenarios run through managed MTP and as a NativeAOT MTP executable. Both TRX reports must contain two passing tests covering FEAT-13 and FEAT-15. | Repeat with `bash scripts/verify-aot.sh` when changing parser, generator, conversion, help, completion, packs, directives or invocation behavior. |
| Generated `02-Commands/Declarative`, Linux x64 | Managed trimmed and NativeAOT publishes ran the generated Commands model with warnings treated as errors. | Run `bash scripts/verify-aot.sh` when changing the generator or example model. |
| Explicit `02-Commands/Manual` without a generator reference, Linux x64 | Managed trimmed and NativeAOT publishes ran the explicit Commands model with warnings treated as errors. | Run `bash scripts/verify-aot.sh` when changing the runtime or example model. |
| Generated and explicit `12-BuiltInTypes`, Linux x64 | Both variants passed distinct managed trimmed and NativeAOT publications, exercising every built-in scalar and representative nullable/collection/union values plus invalid input. | Run `bash scripts/verify-aot.sh` when changing conversions or example models. |
| Generated and explicit `13-CustomConverters`, Linux x64 | Both variants passed distinct managed trimmed and NativeAOT publications, exercising type/provider registration, property overrides, positional/directive conversion and invalid input without handler dispatch. | Run `bash scripts/verify-aot.sh` when changing converters, generation, directives or example models. |
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

Converter providers, generated enum maps, source union case constructors,
definition overrides and model/host registration are additionally governed by
[ADR-015](../Architecture/ADR-015-Converter-Ownership-And-Unions.md).
`AotConversionScenario` accesses generated and explicit union values,
nullable custom collections, a local hexadecimal converter, `Half`, `DateOnly`
and a lexical `FileInfo`. These additions passed the managed and native focused
tests under `artifacts/aot-verification/linux-x64/20261122T170808Z/`, separately
from historical executions above.

## Metrics Impact
**Instruments:** none - planned compatibility validation adds no runtime metrics.

## Verification

`bash scripts/verify-aot.sh` performs locked restore, strict Release builds,
managed trimmed publish/run and NativeAOT publish/run for both Commands
examples and all four built-in/custom converter examples. It
also runs the focused AOT tests through managed MTP and as a NativeAOT MTP
executable, imports both TRX reports, and requires exact two-test parity. The
focused scenario checks valid nested alias binding, inherited and nested pack
values, custom and enum conversion, nullable values, help/completion without
handler dispatch, invalid input without dispatch, and sequential invocation
isolation. Both published example modes also check that the predefined debug
and shell completion directives are registered and execute a completion request.

The converter/union implementation passed the full `bash scripts/verify.sh`
gate with 159 Release tests and fresh Linux x64 evidence under
`artifacts/aot-verification/linux-x64/20261122T170808Z/`. The managed and native
focused runs each passed two tests with exact requirement metadata parity.
The four new examples passed 56 output/exit process checks across eight
distinct trimmed/native publications, with commands, source/lock hashes and
published artifact hashes retained under `conversion-examples/`. Those
process checks are smoke evidence, not MSTest or Governance TRX acceptance.
The gate log is `artifacts/verification-conversion-unions.log`.

The previous Todo.Cli and FileTool.Cli gate was observed on Ubuntu 26.04,
.NET SDK 11.0.100, Clang 21.1.8, RID `linux-x64`. The managed and native
focused executions each reported two passing tests, covering FEAT-13 and
FEAT-15. The gate imported four FEAT-13 requirements and three FEAT-15
requirements from both reports. The following artifact SHA-256 values are
historical evidence for that run, not evidence for the replacement examples:

| Consumer | Trimmed managed DLL | Native executable |
| --- | --- | --- |
| Todo.Cli | `12e62b0fae8ee96f5302a080b917436b51f2e8388568d220fe4cecb2b9dbe48f` | `9d6cac7becedc7209d2b86ae7dff894e776f5604c8611f8c76d72c4ed894c2ed` |
| FileTool.Cli | `3a6881a658aaad8e81bb645ff67bed59aa8f693da98311fb221ace1ae465bdfd` | `cabe360b99e928e4884d05d683c62f2e93acf9759151f0b8d107bae513853f84` |
| Mauve.Cli.Aot.Tests | managed MTP TRX evidence | `088c0cd9d0ebbb2e57f270b50204ccc1fef3714fd4686ada1e4f7801df7750d7` |

On 2026-11-22, `bash scripts/verify.sh` ran the replacement Commands examples
and the focused tests on Linux x64. Both examples passed trimmed and NativeAOT
execution, including the alias, invalid input and completion checks. The
managed and native focused executions each reported two passing tests with
the expected FEAT-13 and FEAT-15 metadata. The run retained logs and hashes
under `artifacts/aot-verification/linux-x64/20261122T115415Z/`:

| Consumer | Trimmed managed DLL | Native executable |
| --- | --- | --- |
| Commands.Declarative | `bffe997bd464e128b5ba864eeb3843512547a82c14aed976e2f139b2a19f6ff5` | `9e19244d2114e079ab2d9e17301a22db902de82ad551d452ab8a55ecfed07b85` |
| Commands.Manual | `52944f521b897602cf44109a01401842d74292fea5bd83ffe0c082bcc2aa4287` | `d304a0c376a418087dac52e0a16701f7cb36024eeba1a581d093ada9a57e9ca6` |
| Mauve.Cli.Aot.Tests | managed MTP TRX evidence | `31d22cf69104e2204bb6bea6321bba73c38e92c34a254a65b19178ec4fe10b65` |

The metadata-bearing behavioral tests are
`AotAcceptanceTests.VerifyGeneratedAndExplicitScenarios` and
`AotAcceptanceTests.VerifyGeneratedAndExplicitDirectives`. ARM64 and other
RIDs remain unverified.

## Diagrams

```mermaid
flowchart LR
    Generator["Generator on build host"] --> Source["Generated source"]
    Source --> Executable["Trimmed NativeAOT runtime executable"]
```
