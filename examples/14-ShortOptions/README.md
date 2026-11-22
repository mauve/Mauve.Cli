# 14 - Short options

Both applications define a counting `--verbose`/`-v` option and two boolean
flags, `--all`/`-a` and `--force`/`-f`. Zero-value short options can be
combined; `-vvaf` counts verbosity twice and enables both flags. Counting
options also stack when repeated as separate short or long options.

```sh
dotnet run --project examples/14-ShortOptions/Declarative -- -vvaf
dotnet run --project examples/14-ShortOptions/Manual -- -vvaf
dotnet run --project examples/14-ShortOptions/Declarative -- --verbose --verbose --all --force
dotnet run --project examples/14-ShortOptions/Manual -- --verbose --verbose --all --force
```

Both forms print:

```text
verbosity=2; all=True; force=True
```
