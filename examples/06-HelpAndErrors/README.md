# 06 - Help and Errors

Both commands use the built-in help option, standard diagnostics, and bounded
suggestions for a misspelled visible option:

```sh
dotnet run --project examples/06-HelpAndErrors/Declarative -- --help
dotnet run --project examples/06-HelpAndErrors/Manual -- --count=4
dotnet run --project examples/06-HelpAndErrors/Declarative -- --cont=4
dotnet run --project examples/06-HelpAndErrors/Manual -- --count=bad
```

The generated and explicit models both enable standard help and send malformed
input through the library's normal diagnostic path. Neither project adds a
custom help renderer or changes parser error handling.
