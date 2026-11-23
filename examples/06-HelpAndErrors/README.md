# 06 - Help and Errors

Both commands use the built-in help option, standard diagnostics, and bounded
suggestions for a misspelled visible option:

```sh
dotnet run --project examples/06-HelpAndErrors/Declarative -- --help
dotnet run --project examples/06-HelpAndErrors/Manual -- show --help
dotnet run --project examples/06-HelpAndErrors/Manual -- --count=4
dotnet run --project examples/06-HelpAndErrors/Declarative -- --count=4 --tag=red --tag=blue
dotnet run --project examples/06-HelpAndErrors/Manual -- summary --verbose
dotnet run --project examples/06-HelpAndErrors/Declarative -- --cont=4
dotnet run --project examples/06-HelpAndErrors/Manual -- --count=bad
dotnet run --project examples/06-HelpAndErrors/Declarative -- --count=-1
dotnet run --project examples/06-HelpAndErrors/Manual -- --count=0
dotnet run --project examples/06-HelpAndErrors/Declarative -- '[notice]' --count=4 --mode=fast
dotnet run --project examples/06-HelpAndErrors/Manual -- '[notice]' --count=4 --mode=fast
```

The generated and explicit models both enable standard help and send malformed
input through the library's normal diagnostic path. Neither project adds a
custom help renderer or changes parser error handling.

The declarative command derives its short description from XML `<summary>`,
its long description from XML `<remarks>`, and shell examples from
`<example><code>...</code></example>` tags. The manual command mirrors those
fields with `Description`, `LongDescription`, and `AddExample(...)`. Two
showcases are included: one successful invocation and one invocation preceded
by the custom `[notice]` directive. The first example uses default shell
inference; the second explicitly declares `language="shell"`.

The `--mode` option uses `[CliAllowedValues("safe", "fast")]` in the
declarative variant and `CliValidators.AllowedValues(...)` in the manual
variant. Both place it in an Inspection option pack. The help projection
displays `safe, fast` as allowed values without invoking validators.
The repeatable `--tag` option accumulates values in order. The declarative
binding uses the supported `IReadOnlyList<string>` collection type for `Tags`.
The `show` and `summary` subcommands both accept the root's inherited
`--verbose` option.

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
