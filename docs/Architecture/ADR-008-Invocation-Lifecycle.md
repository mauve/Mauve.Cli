# ADR-008: Invocation lifecycle and host ownership

## Status

Accepted for the parser implementation.

## Context

The parser returns validated values but must not execute a handler for
invalid or help input. Hosts need a reusable invocation path without
transferring process, exception-presentation, or caller-service ownership to
the library.

## Decision

- Add `CliInvoker.InvokeAsync` as the high-level parse-and-dispatch operation.
  Each call parses independently and dispatches at most the selected
  command's handler after validation.
- Expose explicit `Completed`, `InvalidInput`, `HelpRequested`, `NoHandler`
  and `Cancelled` outcomes. Preserve the exact completed handler exit code.
- Give explicit handlers a `CommandInvocationContext` with the selected
  command, validated `ParseResult`, and typed accessors.
- Normalize synchronous void/integer and `Task`/`Task<int>`/
  `ValueTask`/`ValueTask<int>` handlers to an awaited integer result.
  Reject duplicate handler registration and null task results explicitly.
- Check cancellation before parsing, between synchronous parser callbacks
  and stages, and before dispatch. A cooperative handler cancellation maps
  to `Cancelled`; unrelated cancellation exceptions propagate. A normally
  completed handler keeps its result if cancellation arrives after entry.
- Propagate unexpected callback and handler faults without formatting them.
  The library does not install process signal handlers, dispose caller-owned
  services, or terminate the process.

## Consequences

Hosts own output, exception presentation, cancellation-source lifetime and
process exit mapping. Parsing remains separately usable without invocation.
Generated method binding and supported declarative signatures remain owned
by [FEAT-11](../Requirements/Planned/FEAT-11-Declarative-Api-And-Generation.md).

Affected requirement:
[FEAT-09](../Requirements/FEAT-09-Invocation-Lifecycle.md).
