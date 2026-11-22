# 03 - Values

Each project parses a required integer option (`--count`, alias `-c`), an
optional boolean flag, and a positional name. The `--` marker ends option
parsing, so a name beginning with `-` is bound as a positional. Invalid
integer input is rejected rather than substituted with a default.

```sh
dotnet run --project examples/03-Values/Declarative -- show -c 3 --enabled -- -Ada
dotnet run --project examples/03-Values/Manual -- show -c 3 --enabled -- -Ada
```
