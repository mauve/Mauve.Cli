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
- Use the same `CliSyntaxOptions` as the parser for configured prefixes,
  short aliases, zero-value short clusters and explicitly declared counting
  options.
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
- Keep shell adapters outside the logical-token core. `CompleteForShellAsync`
  remains an unsupported core placeholder; the opt-in completion directive
  emits registration scripts for Bash, Zsh, Fish and PowerShell.
- The shell adapter's narrow raw-payload decoder removes the executable and
  translates UTF-16 cursor positions to the existing logical-token API using
  the quote and escape grammar specified in FEAT-10.
- Shell registration uses a private versioned request and base64 candidate
  protocol. The shared bounded decoder rejects shell expansion, operators,
  unsupported shell-specific escapes, and, except in PowerShell, active-token
  suffixes rather than
  executing input or guessing at its meaning. Bash uses shell-escaped literals
  and supports a limited open-quote form and unquoted escapes, but rejects
  mixed, closed, or escaped quoted active words. The current adapters
  deliberately support a subset of each shell's grammar; a complete native
  lexer and arbitrary mid-token replacement are future work.
- Retire the separate `[suggest:NN]` directive and its dotnet-suggest-style
  wire protocol; shell integration uses `[completion:<shell>]` instead.

## Consequences

The core remains usable by hosts that already own shell parsing, and provider
work remains application-controlled. Shell adapters have documented syntax
limits and do not imply a hard time limit for non-cooperative callbacks.

Affected requirement:
[FEAT-10](../Requirements/FEAT-10-Completion.md).
