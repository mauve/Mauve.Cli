# Mauve.Cli

Mauve.Cli is a .NET library for typed command-line applications. You can
define commands in C# or describe them with attributes and use the source
generator. Both approaches use the same parser.

## Features

- Nested commands, aliases, options, and positional arguments.
- Typed values, defaults, validation, and clear parse errors.
- Built-in help and asynchronous command handlers.
- Reusable nested option packs.
- Shell-independent command, option, and value completion.
- Generated bindings with no runtime generator dependency.
- Trimming and NativeAOT support verified on Linux x64.

Other runtime targets are not yet verified. Shell adapters are not included.
The library is not published as a NuGet package yet.

## Try the examples

```sh
dotnet run --project examples/Todo.Cli -- add "Buy milk"
dotnet run --project examples/Todo.Cli -- --help
dotnet run --project examples/FileTool.Cli -- copy source.txt backup.txt
```

`Todo.Cli` uses generated commands. `FileTool.Cli` builds a command model
directly. These examples print the requested action; they do not manage files
or save tasks.

## Define a command directly

```csharp
using System;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;

OptionDefinition name = new("--name", typeof(string), OptionArity.RequiredScalar, isRequired: true);
CommandModel model = CommandModel.Create(new CommandBuilder("greet").AddOption(name));
ParseResult result = new CliParser().Parse(model, ["--name", "Ada"]);

if (result.Outcome == ParseOutcome.Validated
    && result.TryGetOptionValue(name, out string? value))
{
    Console.WriteLine($"Hello, {value}!");
}
```

## Generate a command

```csharp
using System;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;

[CliCommand(IsRoot = true)]
public partial class GreetCommand
{
    [CliOption(IsRequired = true)]
    public partial string Name { get; }

    [CliHandler]
    private int Run()
    {
        Console.WriteLine($"Hello, {Name}!");
        return 0;
    }
}
```

The generator adds `GreetCommand.CreateModel()`, typed bindings, and handler
dispatch. It derives `greet` and `--name` from the C# identifiers and derives
the scalar arity from `string`. Option packs can group reusable options in
either API.

## Trimming and NativeAOT

Linux x64 is verified for both generated and direct models. Run
`bash scripts/verify-aot.sh` to repeat the trimmed and NativeAOT publish/run
checks. The gate runs a focused parser test as a native MSTest executable and
also runs both published example apps. ARM64 and other targets remain
unverified.

See the [examples](examples/), [feature specifications](docs/Requirements/README.md),
and [contributor guide](CONTRIBUTING.md) for more information.
