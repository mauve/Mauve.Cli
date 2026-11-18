# Mauve.Cli

Mauve.Cli is a .NET library for building typed command-line applications. It
provides an explicit command model and a source-generated declaration API.
Both use the same parser and invocation pipeline.

## Highlights

- Nested commands, aliases, options, and positional arguments.
- Typed conversion, defaults, validation, and structured parse errors.
- Help documents and asynchronous command handlers.
- Reusable nested option packs with invocation-local values.
- Shell-independent command, option, enum, and provider-based completion.
- Generated command models with no runtime generator dependency.
- Bounded model and input processing.

Shell adapters and end-to-end NativeAOT verification are not yet supported.

## Explicit model

Build a command model, parse the argument tokens, and read typed values:

```csharp
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;

CommandModel model = CommandModel.Create(
    new CommandBuilder("greet")
        .AddOption(new OptionDefinition(
            "--name",
            typeof(string),
            OptionArity.RequiredScalar,
            isRequired: true)));

ParseResult result = new CliParser().Parse(model, ["--name", "Ada"]);
if (result.Outcome == ParseOutcome.Validated
    && result.TryGetOptionValue(model.Root.Options[0], out string? name))
{
    Console.WriteLine($"Hello, {name}!");
}
```

## Generated commands

Declare commands and options on partial classes. The generator creates the
model factory and typed property bindings:

```csharp
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;

[CliCommand("greet", IsRoot = true)]
public partial class GreetCommand
{
    [CliOption("--name", OptionArity.RequiredScalar, 0, IsRequired = true)]
    public partial string Name { get; }

    [CliHandler]
    private int Run() => Name.Length;
}
```

Call `GreetCommand.CreateModel()` to create the model, then pass it to
`CliInvoker` with the user's argument tokens.

## Option packs

Option packs group reusable options and can contain nested packs. Explicit
models use `OptionPackBuilder`; generated declarations use `CliOptionPack`,
`CliPack`, and `CliPackOption`. A generated handler can read pack values from
its `CommandInvocationContext`.

See the [feature index](docs/Requirements/README.md) for implementation
status and the [architecture decisions](docs/Architecture/) for API details.
