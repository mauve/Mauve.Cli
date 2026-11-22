# Mauve.Cli

Mauve.Cli is a .NET library for typed command-line applications. You can
define commands in C# or describe them with attributes and use the source
generator. Both approaches use the same parser.

## Features

- Nested commands, aliases, options, and positional arguments.
- Typed values, defaults, validation, and clear parse errors.
- Built-in help and asynchronous command handlers.
- Root directives that run before command dispatch.
- Opt-in debugger-wait and shell-completion directives.
- Reusable nested option packs.
- Shell-independent command, option, and value completion.
- Opt-in Bash, Zsh, Fish, and PowerShell completion with documented syntax
  limits.
- Generated bindings with no runtime generator dependency.
- Trimming and NativeAOT support verified on Linux x64.

Other runtime targets are not yet verified. The library is not published as a
NuGet package yet.

## Try the examples

```sh
dotnet run --project examples/Todo.Cli -- add "Buy milk"
dotnet run --project examples/Todo.Cli -- --help
dotnet run --project examples/FileTool.Cli -- copy source.txt backup.txt
dotnet run --project examples/Todo.Cli -- '[completion:bash]'
```

`[debug]` waits for a debugger to attach. `[completion:bash]` emits a Bash
completion registration script.

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
    [PredefinedDirectives.CreateDebug(), PredefinedDirectives.CreateCompletion("greet")]);
```

The completion directive writes through the `CliHostOptions` writers, defaulting
to standard output and error. The synchronous `Run` overload accepts only
synchronous additional directives; use `RunAsync` for these built-ins.
An explicit `null` as the second argument is ambiguous with the directive-list
overload; omit it or use `options: null` to select the host-options overload.

## Shell completion

Register the completion directive with the executable name users invoke:

```csharp
return await CliInvoker.RunAsync<GreetCommand>(args,
    [PredefinedDirectives.CreateCompletion("greet")]);
```

For an explicit model, add the same directive with
`root.AddDirective(PredefinedDirectives.CreateCompletion("greet"))` before
creating the model. The optional `service` and `providers` arguments configure
the existing completion engine and its value providers.

Add the appropriate line to your shell profile (`~/.bashrc`, `~/.zshrc`,
`~/.config/fish/config.fish`, or PowerShell's `$PROFILE`), replacing `greet`
with the installed command name:

```bash
# Bash
eval "$(greet '[completion:bash]')"
# Zsh
eval "$(greet '[completion:zsh]')"
```

```fish
greet '[completion:fish]' | source
```

```powershell
greet '[completion:powershell]' | Out-String | Invoke-Expression
```

Reload the profile or start a new shell. The command must be on `PATH`; aliases
and wrappers require their own registered command name. The scripts should be
loaded only from a trusted executable. Bash 4 or later is required; Bash, Zsh
and Fish also require `base64`. Zsh initializes its completion system when needed. Remove the profile
line and restart the shell to disable registration.

The adapters currently reject shell expansion, operators, control characters,
and shell-specific escape forms. Bash, Zsh and Fish reject completion in the
middle of an active word. Bash accepts a simple open quote at the start of a
word or after `=`, but rejects mixed or closed quotes and backslash-escaped
quoted active words. Bash also rejects unquoted colons or multiple equals signs
in the active word. If `=` is removed from Bash's `COMP_WORDBREAKS`, quoted
active words containing `=` are also rejected.
Rejected requests produce no
candidates and a diagnostic on standard error; provider failures and
unsupported shell names also fail rather than returning partial results. The
adapter does not display candidate descriptions. `CompletionService` remains
available for hosts which already provide logical argument tokens and need
replacement ranges directly.

For generated commands, obtain an option or positional definition from
`GreetCommand.CreateModel()` to register its provider; generated definitions
retain their identity when `CliInvoker.RunAsync<GreetCommand>` builds the
invocation model. Explicit models can register the same definition handles
they add to their builders.

## Trimming and NativeAOT

Linux x64 is verified for both generated and direct models. Run
`bash scripts/verify-aot.sh` to repeat the trimmed and NativeAOT publish/run
checks. The gate runs a focused parser test as a native MSTest executable and
also runs both published example apps. ARM64 and other targets remain
unverified.

See the [examples](examples/), [feature specifications](docs/Requirements/README.md),
and [contributor guide](CONTRIBUTING.md) for more information.
