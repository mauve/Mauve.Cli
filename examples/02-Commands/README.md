# 02 - Commands

Both projects define `greet` (also `hi`) and `goodbye` subcommands with one
required name argument. They print the selected message and do not change
state, and register the built-in debug and completion directives.

```sh
dotnet run --project examples/02-Commands/Declarative -- hi Ada
dotnet run --project examples/02-Commands/Manual -- hi Ada
dotnet run --project examples/02-Commands/Declarative -- '[completion:bash]'
dotnet run --project examples/02-Commands/Manual -- '[completion:bash]'
```
