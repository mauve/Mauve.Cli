# 05 - Validation

Both projects accept `--limit <integer>` and use 10 when it is omitted.
Invalid integer text is rejected by parsing. The Manual model also validates
that the converted value is between 1 and 50; an out-of-range value produces
a model-validation diagnostic:

```sh
dotnet run --project examples/05-Validation/Declarative --
dotnet run --project examples/05-Validation/Manual -- --limit=25
dotnet run --project examples/05-Validation/Declarative -- --limit=bad
dotnet run --project examples/05-Validation/Manual -- --limit=99
```

The declarative property initializer supplies the model-level default and help
display. Declarative validators are not inferred, so the generated example
applies its range check in the handler. Its range rejection is an
application-level message, not a parser diagnostic; the Manual project
demonstrates model-level validation. Their behavior intentionally differs for
out-of-range values.
