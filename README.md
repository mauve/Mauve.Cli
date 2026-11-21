# Mauve.Cli

Mauve.Cli is a .NET library for typed command-line applications. You can
define commands in C# or describe them with attributes and use the source
generator. Both approaches use the same parser.

## Features

- Nested commands, aliases, options, and positional arguments.
- Typed values, defaults, validation, and clear parse errors.
- Built-in help and asynchronous command handlers.
- Root directives that run before command dispatch.
- Opt-in debugger-wait and dotnet-suggest-style completion directives.
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
dotnet run --project examples/Todo.Cli -- '[suggest:7]' 'todo ad'
```

`[debug]` waits for a debugger to attach. `[suggest:NN]` treats `NN` as a
UTF-16 cursor offset in the supplied complete command-line payload and prints
matching insertion texts, one per line. Both directives precede the command;
the suggest protocol does not provide shell-specific quoting or adapters.

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
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;

[CliCommand(IsRoot = true)]
public partial class GreetCommand
{
    [CliOption(IsRequired = true)]
    public partial string Name { get; }

    [CliHandler]
    private async Task<int> Run()
    {
        await Task.Yield();
        Console.WriteLine($"Hello, {Name}!");
        return 0;
    }
}
```

The generator adds `GreetCommand.CreateModel()`, typed bindings, and handler
dispatch. It derives `greet` and `--name` from the C# identifiers and derives
the scalar arity from `string`. Option packs can group reusable options in
either API. Generated handlers may return `void`, `int`, `Task`, `Task<int>`,
`ValueTask`, or `ValueTask<int>`; async-void handlers are unsupported.

Generated roots can add predefined directives at the host entry point:

```csharp
return await CliInvoker.RunAsync<GreetCommand>(args,
    [PredefinedDirectives.CreateDebug(), PredefinedDirectives.CreateSuggest()]);
```

The suggest directive writes through the `CliHostOptions` writers, defaulting
to standard output and error. The synchronous `Run` overload accepts only
synchronous additional directives; use `RunAsync` for these built-ins.
An explicit `null` as the second argument is ambiguous with the directive-list
overload; omit it or use `options: null` to select the host-options overload.

## Trimming and NativeAOT

Linux x64 is verified for both generated and direct models. Run
`bash scripts/verify-aot.sh` to repeat the trimmed and NativeAOT publish/run
checks. The gate runs a focused parser test as a native MSTest executable and
also runs both published example apps. ARM64 and other targets remain
unverified.

See the [examples](examples/), [feature specifications](docs/Requirements/README.md),
and [contributor guide](CONTRIBUTING.md) for more information.
