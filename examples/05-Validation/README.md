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

The declaration attributes currently have no model-level defaults or
validators. Accordingly, the generated example applies its fallback and range
check in the handler. Its range rejection is an application-level message, not
model validation and not a parser diagnostic; the Manual project demonstrates
actual model-level defaulting and validation. Their behavior intentionally
differs for out-of-range values.
