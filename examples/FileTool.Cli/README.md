# FileTool.Cli

This example builds its command model with the explicit API. It does not use
the source generator or change files:

```sh
dotnet run --project examples/FileTool.Cli -- copy source.txt backup.txt
dotnet run --project examples/FileTool.Cli -- copy --overwrite source.txt backup.txt
dotnet run --project examples/FileTool.Cli -- copy --help
```

The command prints the requested operation. It does not copy or overwrite
files.
