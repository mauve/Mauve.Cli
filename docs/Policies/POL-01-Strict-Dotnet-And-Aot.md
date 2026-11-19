# POL-01: Strict .NET and NativeAOT bootstrap policy

## Owner and scope

Owner: Mauve.Cli maintainers. Applies to `src/`, `tests/`, `examples/`,
their project files and `scripts/verify*.sh`.
This consumer-owned extension annotates `MGOV-POL-002`, `MGOV-POL-003`,
`MGOV-POL-005` and `MGOV-POL-007`; it does not modify managed policy.

## Rules and rationale

- Pin SDK 11.0.100 and C# 15.0. Product/tests target `net11.0`; only the
  build-time Roslyn component targets `netstandard2.0`.
- Require nullable checking, deterministic builds, all SDK analysis rules,
  compiler/code-style/analyzer warnings as errors, and public XML docs.
  Centralize package versions and retain reviewed package lock files.
- The managed root `.editorconfig` remains untouched. Consumer-owned nested
  configs impose four-space formatting, explicit types/accessibility,
  file-scoped namespaces, braces, naming rules and no unused imports.
  Source/tests/examples share that strict baseline.
- Tests disable CA1515 and public-member XML documentation because MSTest
  requires public fixtures. Test-only naming, ignored assertion return values
  and inline test-data arrays have narrow analyzer exceptions. Example console
  output and generated-only declarations also carry narrow suppressions.
  These are not product-code analyzer waivers.
- Tests use MSTest on Microsoft Testing Platform, explicit imports and
  assembly parallelization.
  Future tests declare one owning active feature and matching requirements
  using native method-level `TestProperty`/Governance metadata. Do not use
  synthetic passing assertions or planned IDs as implementation evidence.
- The runtime enables AOT compatibility analysis. Examples enable NativeAOT
  and treat trimming/AOT warnings as errors. Generated implementation must
  not become a runtime generator dependency.
- No process-wide or privileged test environment is required for portable
  product tests. Native publication is environment-gated by native compiler,
  linker, development headers and host runtime packs.

## Enforcement and evidence

`bash scripts/verify.sh` is the local fail-closed gate: strict Governance
validation/source-only generation; locked restore; Release build; format
verification; the full MTP test suite; separate real metadata/TRX probe and expected
MGTEST001/IDE0161 rejections; trimmed and NativeAOT publish/run acceptance;
no-op Governance update check. `scripts/verify-aot.sh` can run the
environment-gated native acceptance independently.

The compiler supplies CS/CA/IDE diagnostics; Governance supplies MGTEST and
MGLOG analyzer diagnostics. No parser diagnostic namespace or telemetry
instruments are allocated by this bootstrap.

These are executable local checks, **not protected required CI**. Hosted
Governance/release execution and package publication are not provisioned.
See [ADR-002](../Architecture/ADR-002-Governance-Enrollment.md).
Only Linux x64 with .NET SDK 11.0.100 and Clang 21.1.8 is verified for real
trimmed and NativeAOT parser behavior. ARM64 and all other RIDs remain
unverified; see
[FEAT-13](../Requirements/FEAT-13-NativeAot-And-Trimming.md) and
[ADR-013](../Architecture/ADR-013-NativeAot-Support-Matrix.md).

The strict solution build and 83-test MTP suite pass locally. The independent
probe's TRX lives under `artifacts/obj/governance-probe/`; it must not populate
the active parser ledger or source-only index.
