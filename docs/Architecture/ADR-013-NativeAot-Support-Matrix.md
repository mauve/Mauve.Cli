# ADR-013: NativeAOT support matrix

## Status

Accepted.

## Context

FEAT-13 requires real trimmed and NativeAOT publishes and executions for
generated and explicit consumers. The local verification host is Ubuntu
26.04 x64 with .NET SDK 11.0.100 and Clang 21.1.8. ARM64 execution is not
available in this environment.

## Decision

- Linux x64 is the first verified NativeAOT and trimming target.
- The generated `Todo.Cli` and explicit `FileTool.Cli` consumers must each
  pass small managed trimmed and NativeAOT publish/run checks using their
  normal user-facing models, with linker and compiler warnings treated as
  errors.
- `Mauve.Cli.Aot.Tests` must run the comprehensive generated and explicit
  parser matrix through managed MTP and as a NativeAOT MTP executable. Both
  runs must report exactly two passing tests, covering FEAT-13 and FEAT-15,
  with each feature's declared requirements.
- `scripts/verify-aot.sh` is the reproducible local gate. It requires a native
  Linux x64 host, performs locked restores, preserves command output and
  environment/source/artifact hashes, and rejects generator assemblies in
  published outputs.
- ARM64 and all other RIDs remain unverified. Cross-publishing without running
  the target executable does not expand the support matrix.

## Consequences

The support claim is limited to the exact RID and acceptance scope in
[FEAT-13](../Requirements/FEAT-13-NativeAot-And-Trimming.md). Changes to the
runtime, generator, converter registration, or representative consumers
require rerunning the gate. Native tests supplement rather than replace final
application artifact execution. Application-provided callbacks remain the
application's trimming/AOT responsibility.
