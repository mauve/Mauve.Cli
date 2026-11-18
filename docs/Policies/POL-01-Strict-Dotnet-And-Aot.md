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
- Two narrow exceptions are intentional: tests disable CA1515 (VSTest's
  public MSTest fixtures in executable Test SDK assemblies), and each
  example's `Main` has a justified CA1303 suppression only for its English
  not-implemented bootstrap marker. These are not blanket analyzer waivers.
- Tests use MSTest/VSTest, explicit imports and assembly parallelization.
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
verification; product test discovery; separate real metadata/TRX probe and
expected MGTEST001/IDE0161 rejections; native scaffold publish/run; no-op
Governance update check.

The compiler supplies CS/CA/IDE diagnostics; Governance supplies MGTEST and
MGLOG analyzer diagnostics. No parser diagnostic namespace or telemetry
instruments are allocated by this bootstrap.

These are executable local checks, **not protected required CI**. Hosted
Governance/release execution and package publication are not provisioned.
See [ADR-002](../Architecture/ADR-002-Governance-Enrollment.md).
Only Linux ARM64/GCC is initially verified. Other hosts and real parser AOT
behavior remain unverified; see
[FEAT-13](../Requirements/Planned/FEAT-13-NativeAot-And-Trimming.md).

The parser now has product test methods, but no product MSTest run is claimed
until restore and the required Governance feed succeed. A missing test run is
an evidence gap, not a pass. The independent probe's TRX lives under
`artifacts/obj/governance-probe/`; it must not populate the active parser
ledger or source-only index.
