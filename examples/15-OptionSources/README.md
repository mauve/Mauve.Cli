# 15 - Option sources

Both applications print where each option value came from. `--mode` uses its
declared default, `--region` reads `SOURCE_REGION`, and required `--profile`
must be supplied on the command line. The output shows each option's
`ValueOrigin` without printing its value.

```sh
SOURCE_REGION=west dotnet run --project examples/15-OptionSources/Declarative -- --profile local
SOURCE_REGION=west dotnet run --project examples/15-OptionSources/Manual -- --profile local
```

Both forms print:

```text
--mode: Default
--region: Environment
--profile: Explicit
```
