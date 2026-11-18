# Todo.Cli

This example uses generated command declarations:

```sh
dotnet run --project examples/Todo.Cli -- add "Buy milk"
dotnet run --project examples/Todo.Cli -- new "Call Sam"
dotnet run --project examples/Todo.Cli -- list
dotnet run --project examples/Todo.Cli -- --help
```

The `add` and `new` names share one command. The sample prints the selected
action and does not store tasks.
