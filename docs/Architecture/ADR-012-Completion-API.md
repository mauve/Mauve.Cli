# ADR-012: Shell-independent completion API

## Status

Accepted for the core logical-token completion API.

## Context

Completion needs cursor-aware command, option and value suggestions without
running normal invocation callbacks. Shell quoting, tokenization and execution
belong to host adapters and must not be inferred by the core.

## Decision

- Accept already-tokenized arguments and a UTF-16 token index/offset through
  `CompletionRequest`. Tokens after the cursor do not affect context.
- Return logical `CompletionCandidate` values with explicit replacement
  ranges. The core does not quote or evaluate candidate text.
- Resolve completed command and option context using the validated model and
  visible scope. Do not construct command instances or run default factories,
  validators or handlers.
- Register asynchronous providers by definition handle, with explicit option
  or positional dependencies. Providers receive only typed values that are
  available, non-sensitive and explicitly listed.
- Invoke built-in converters and enum maps during context resolution. A
  custom converter runs only when registered with
  `ConverterRegistryBuilder.RegisterCompletionSafe`.
- Bound provider enumeration to 512 raw candidates, 128 emitted candidates
  and 1024 UTF-16 code units per candidate. Return explicit cancellation,
  provider-failure, context-failure and limit outcomes; never turn a failure
  into an empty successful result.
- Do not implement shell adapters in the core. `CompleteForShellAsync`
  reports `UnsupportedShell` until an adapter has an approved per-shell
  tokenization and escaping contract.
- The opt-in FEAT-15 `[suggest:NN]` directive provides a narrow raw-payload
  adapter with the quote and escape grammar specified in FEAT-10. It removes
  the executable and translates UTF-16 cursor positions to the existing
  logical-token API. It is not a shell adapter and does not execute text.

## Consequences

The API is safe for hosts that already own shell parsing, and provider work
remains application-controlled. Completion support does not imply a shell
integration or a hard time limit for non-cooperative callbacks.

Affected requirement:
[FEAT-10](../Requirements/FEAT-10-Completion.md).
