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
  pass managed trimmed and NativeAOT publish/run scenarios with linker and
  compiler warnings treated as errors.
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
require rerunning the gate. Application-provided callbacks remain the
application's trimming/AOT responsibility.
