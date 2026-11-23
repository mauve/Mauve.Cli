# 16 - Exception handling

Two self-contained applications show the same setup: define a command,
configure exception handling with `CliHostOptionsBuilder`, and call
`CliInvoker.RunAsync`. `Declarative` uses attributes; `Manual` builds the model
directly. Neither application accesses files or services.

```sh
dotnet run --project examples/16-ExceptionHandling/Declarative -- --scenario=direct
dotnet run --project examples/16-ExceptionHandling/Manual -- --scenario=missing
```

Run either version with these arguments:

| Arguments | Exit code | Output |
| --- | --- | --- |
| No arguments, or `--scenario=success` | 0 | `Completed.` on stdout |
| `--scenario=direct` | 65 | `Error: The requested operation cannot be completed.` on stderr |
| `--scenario=missing` | 74 | `Error: The requested file is unavailable.` on stderr |
| `--help` | 0 | Built-in help |

The direct scenario throws `CliExitCodeException` with its own exit code and
safe message. The missing scenario throws a simulated `FileNotFoundException`;
the central `MapException<IOException>` registration handles it without
displaying private path details.

`WithExceptionRenderer` replaces the default renderer with an `Error:` prefix.
Remove that call to print only the safe message. Custom renderers own redaction;
do not print raw exception messages, stacks or inner exceptions.

Unmapped exceptions still propagate, and low-level `InvokeAsync` does not apply
host exception mappings. `RunAsync` supports synchronous as well as asynchronous
handlers; use `Run` for synchronous entry points when needed.
