using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Aot.Tests;

internal enum Priority
{
    None = 0,
    Low = 1,
    Urgent = 7
}

internal readonly record struct BuildCode(int Value);

internal sealed record ScenarioHandles(
    CommandDefinition Root,
    OptionDefinition Workspace,
    OptionDefinition Verbose,
    OptionDefinition Tag,
    OptionDefinition Priority,
    OptionDefinition BackupPriority,
    OptionDefinition Code,
    OptionDefinition Limit,
    OptionDefinition Retries,
    OptionDefinition Count,
    PositionalDefinition Text);

internal static class AotScenario
{
    private static int _handlerInvocations;

    internal static ConverterRegistry CreateConverters()
    {
        return new ConverterRegistryBuilder()
            .RegisterEnum(
            [
                new KeyValuePair<string, Priority>("low", Priority.Low),
                new KeyValuePair<string, Priority>("urgent", Priority.Urgent)
            ])
            .Register(static (text, _) =>
                int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                    ? ValueConversion.FromValue(new BuildCode(value))
                    : ValueConversion.Failure<BuildCode>("build-code.invalid"))
            .Build();
    }

    internal static ScenarioHandles CreateGeneratedHandles(
        CommandModel model,
        OptionDefinition workspace,
        OptionDefinition verbose,
        OptionDefinition tag,
        OptionDefinition priority,
        OptionDefinition backupPriority,
        OptionDefinition code,
        OptionDefinition limit,
        OptionDefinition retries)
    {
        ArgumentNullException.ThrowIfNull(model);
        CommandDefinition add = model.Root.Children[0];
        return new ScenarioHandles(
            model.Root,
            workspace,
            verbose,
            tag,
            priority,
            backupPriority,
            code,
            limit,
            retries,
            add.Options[0],
            add.Positionals[0]);
    }

    internal static int HandleGenerated(
        CommandInvocationContext context,
        int generatedCount,
        string generatedText,
        OptionDefinition workspace,
        OptionDefinition verbose,
        OptionDefinition tag,
        OptionDefinition priority,
        OptionDefinition backupPriority,
        OptionDefinition code,
        OptionDefinition limit,
        OptionDefinition retries)
    {
        ScenarioHandles handles = new(
            context.ParseResult.SelectedPath[0],
            workspace,
            verbose,
            tag,
            priority,
            backupPriority,
            code,
            limit,
            retries,
            context.Command.Options[0],
            context.Command.Positionals[0]);
        return !context.TryGetOptionValue(handles.Count, out int parsedCount)
            || !context.TryGetPositionalValue(handles.Text, out string? parsedText)
            || parsedCount != generatedCount
            || !string.Equals(parsedText, generatedText, StringComparison.Ordinal)
            ? throw new InvalidOperationException("Generated typed binding differs from the parsed values.")
            : Handle(context, handles);
    }

    internal static CommandModel CreateExplicitModel(out ScenarioHandles handles)
    {
        OptionDefinition workspace = new(
            "--workspace",
            typeof(string),
            OptionArity.RequiredScalar,
            isRequired: true,
            scope: OptionScope.Inherited);
        OptionDefinition verbose = new(
            "--verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            scope: OptionScope.Inherited);
        OptionDefinition tag = new("--tag", typeof(string), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition priority = new("--priority", typeof(Priority), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition backupPriority = new(
            "--backup-priority",
            typeof(Priority?),
            OptionArity.RequiredScalar,
            isRequired: true);
        OptionDefinition code = new("--code", typeof(BuildCode), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition limit = new("--limit", typeof(int?), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition retries = new("--retries", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition count = new("--count", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        PositionalDefinition text = new("text", typeof(string));
        OptionPackBuilder global = new OptionPackBuilder()
            .AddOption(workspace)
            .AddOption(verbose);
        OptionPackBuilder retry = new OptionPackBuilder().AddOption(retries);
        OptionPackBuilder network = new OptionPackBuilder()
            .AddOption(tag)
            .AddOption(priority)
            .AddOption(backupPriority)
            .AddOption(code)
            .AddOption(limit)
            .AddPack("retry", retry, "Retry");

        CommandBuilder root = new CommandBuilder("aot-todo")
            .AddPack("global", global, "Global");
        _ = root.AddChild(new CommandBuilder("add")
            .AddAlias("new")
            .AddOption(count)
            .AddPositional(text)
            .AddPack("network", network, "Network")
            .SetHandler((context, _) => Handle(context, new ScenarioHandles(
                context.ParseResult.SelectedPath[0],
                workspace,
                verbose,
                tag,
                priority,
                backupPriority,
                code,
                limit,
                retries,
                count,
                text))));
        CommandModel model = CommandModel.Create(root, includeStandardHelp: true);
        handles = new ScenarioHandles(
            model.Root,
            workspace,
            verbose,
            tag,
            priority,
            backupPriority,
            code,
            limit,
            retries,
            count,
            text);
        return model;
    }

    internal static int Handle(CommandInvocationContext context, ScenarioHandles handles)
    {
        _handlerInvocations++;
        if (!context.TryGetOptionValue(handles.Count, out int count)
            || !context.TryGetPositionalValue(handles.Text, out string? text))
        {
            throw new InvalidOperationException("The selected command values are unavailable.");
        }

        OptionPackContext global = context.GetOptionPack(context.ParseResult.SelectedPath[0], "global");
        OptionPackContext network = context.GetOptionPack("network");
        OptionPackContext retry = network.GetNestedPack("retry");
        return !global.TryGetOptionValue(handles.Workspace, out string? workspace)
            || !global.TryGetOptionValue(handles.Verbose, out bool verbose)
            || !network.TryGetOptionValue(handles.Tag, out string? tag)
            || !network.TryGetOptionValue(handles.Priority, out Priority priority)
            || !network.TryGetOptionValue(handles.BackupPriority, out Priority? backupPriority)
            || !network.TryGetOptionValue(handles.Code, out BuildCode code)
            || !network.TryGetOptionValue(handles.Limit, out int? limit)
            || !retry.TryGetOptionValue(handles.Retries, out int retries)
            ? throw new InvalidOperationException("The selected option-pack values are unavailable.")
            : count
            + text!.Length
            + workspace!.Length
            + tag!.Length
            + (verbose ? 1 : 0)
            + (int)priority
            + (int)backupPriority!.Value
            + code.Value
            + limit!.Value
            + retries;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1508:Avoid dead conditional code",
        Justification = "The handler count changes through delegate dispatch during invocation.")]
    internal static async Task VerifyAsync(
        CommandModel model,
        ScenarioHandles handles,
        ConverterRegistry converters)
    {
        _handlerInvocations = 0;
        CliInvoker invoker = new(new CliParser(converters));
        string[] firstArguments =
        [
            "new",
            "--workspace=north",
            "--verbose",
            "--tag=alpha",
            "--priority=urgent",
            "--backup-priority=low",
            "--code=42",
            "--limit=20",
            "--retries=2",
            "--count=3",
            "one"
        ];
        InvocationResult first = await invoker.InvokeAsync(model, firstArguments).ConfigureAwait(false);
        Require(first.Outcome == InvocationOutcome.Completed && first.ExitCode == 89,
            "The first generated/explicit invocation did not bind all values.");
        Require(string.Join(" ", first.ParseResult!.SelectedCommand!.Path) == "aot-todo add",
            "The command alias did not resolve to the canonical path.");
        Require(first.ParseResult.TryGetOptionValue(model.Root, handles.Workspace, out string? workspace)
            && workspace == "north",
            "The inherited workspace option was not bound.");

        int calledBeforeInvalid = _handlerInvocations;
        InvocationResult invalidInteger = await invoker.InvokeAsync(
            model,
            [
                "new",
                "--workspace=north",
                "--tag=alpha",
                "--priority=urgent",
                "--backup-priority=low",
                "--code=42",
                "--limit=20",
                "--retries=2",
                "--count=not-an-integer",
                "bad"
            ]).ConfigureAwait(false);
        Require(invalidInteger.Outcome == InvocationOutcome.InvalidInput
            && _handlerInvocations == calledBeforeInvalid,
            "Invalid integer input was dispatched or accepted.");
        InvocationResult unknownOption = await invoker.InvokeAsync(
            model,
            [
                "new",
                "--workspace=north",
                "--tag=alpha",
                "--priority=urgent",
                "--backup-priority=low",
                "--code=42",
                "--limit=20",
                "--retries=2",
                "--count=3",
                "--unknown",
                "bad"
            ]).ConfigureAwait(false);
        Require(unknownOption.Outcome == InvocationOutcome.InvalidInput
            && _handlerInvocations == calledBeforeInvalid,
            "Unknown option input was dispatched or accepted.");

        ParseResult excess = new CliParser(converters).Parse(model, [.. firstArguments, "extra\nline"]);
        Require(excess.Outcome == ParseOutcome.Invalid
            && string.Join(" ", excess.FormatDiagnostics()) == "Argument 12: Unexpected positional argument \"extra\\nline\".",
            "The extra argument was not identified and escaped.");

        InvocationResult help = await invoker.InvokeAsync(model, ["new", "--help"]).ConfigureAwait(false);
        Require(help.Outcome == InvocationOutcome.HelpRequested
            && _handlerInvocations == calledBeforeInvalid,
            "Help unexpectedly invoked the command handler.");

        string activeToken = "--priority=ur";
        CompletionResult completion = await new CompletionService(converters).CompleteAsync(
            new CompletionRequest(
                model,
                ["new", activeToken],
                1,
                activeToken.Length)).ConfigureAwait(false);
        Require(completion.Outcome == CompletionOutcome.Completed
            && completion.Candidates.Count == 1
            && completion.Candidates[0].InsertionText == "urgent"
            && _handlerInvocations == calledBeforeInvalid,
            "Completion did not suggest the explicit enum name without dispatching.");

        InvocationResult second = await invoker.InvokeAsync(
            model,
            [
                "add",
                "--workspace=west",
                "--tag=beta",
                "--priority=low",
                "--backup-priority=urgent",
                "--code=43",
                "--limit=10",
                "--retries=4",
                "--count=5",
                "two"
            ]).ConfigureAwait(false);
        Require(second.Outcome == InvocationOutcome.Completed && second.ExitCode == 81,
            "The second invocation reused values from the first.");
        Require(_handlerInvocations == 2, "Unexpected command handler invocation count.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
