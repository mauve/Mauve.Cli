# 10 - Directives

Both projects register a custom `[notice]` directive and the predefined shell
completion directive. The custom directive runs before the selected command;
the command only prints a status message.

```sh
dotnet run --project examples/10-Directives/Declarative -- '[notice]' run
dotnet run --project examples/10-Directives/Manual -- '[notice]' run
dotnet run --project examples/10-Directives/Declarative -- '[completion:bash]'
```

`Declarative` uses a generated root-method directive. `Manual` registers the
equivalent handler with `DirectiveBuilder` and has no generator reference.
