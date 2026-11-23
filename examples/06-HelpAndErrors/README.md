# 06 - Help and Errors

Both commands use the built-in help option, standard diagnostics, and bounded
suggestions for a misspelled visible option:

```sh
dotnet run --project examples/06-HelpAndErrors/Declarative -- --help
dotnet run --project examples/06-HelpAndErrors/Manual -- --count=4
dotnet run --project examples/06-HelpAndErrors/Declarative -- --cont=4
dotnet run --project examples/06-HelpAndErrors/Manual -- --count=bad
dotnet run --project examples/06-HelpAndErrors/Declarative -- --count=-1
dotnet run --project examples/06-HelpAndErrors/Manual -- --count=0
```

The generated and explicit models both enable standard help and send malformed
input through the library's normal diagnostic path. Neither project adds a
custom help renderer or changes parser error handling.

Negative item numbers throw `CliExitCodeException` with exit code 65 and an
explicitly safe public message. Item zero throws a simulated
`FileNotFoundException`; the central `IOException` mapping returns 74 without
displaying private lookup details. No files are accessed.

Both hosts replace the default exception renderer to prefix safe messages
with `inspect:`. The declarative example uses `CliHostOptionsBuilder`; the
explicit example uses the equally supported constructor and an immutable
registry. Either configuration style works with either API.

Mappings select the most-specific registered exception class, independent
of registration order. Explicit registrations override a
`CliExitCodeException`'s own result. Unmapped faults propagate unchanged.
The default renderer prints only the mapped safe message; custom renderers
can inspect the original exception through their context and must redact
secrets, stacks and inner exception details themselves. Empty rendered text
suppresses output; null text or renderer/writer faults propagate.

Only high-level `Run`/`RunAsync` handles exceptions. `InvokeAsync` continues to
propagate faults even if its host options contain mappings, and direct
`CliParser.Parse` remains unaffected. Caller cancellation retains its existing
`Cancelled` outcome. No entry point terminates the process.
