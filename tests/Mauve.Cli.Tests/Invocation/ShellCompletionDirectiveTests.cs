using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class ShellCompletionDirectiveTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-008")]
    public async Task Completion_DoesNotRegisterRetiredSuggestDirective()
    {
        int dispatched = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddDirective(PredefinedDirectives.CreateCompletion("todo"))
            .SetHandler((_, _) => { dispatched++; return 0; }));

        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, ["[suggest:7]", "todo ad"], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.InvalidInput, result.Outcome);
        Assert.AreEqual(0, dispatched);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-008")]
    public async Task Completion_EmitsRegistrationForEachSupportedShell()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddDirective(PredefinedDirectives.CreateCompletion("todo")));
        CliHostOptions options = new(output, errors);
        foreach (string shell in new[] { "bash", "zsh", "fish", "powershell" })
        {
            int code = await CliInvoker.RunAsync(model,
                [$"[completion:{shell}]"], options, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(0, code);
            StringAssert.Contains(output.ToString(), "todo", StringComparison.Ordinal);
            StringAssert.Contains(output.ToString(), "query-v1", StringComparison.Ordinal);
            output.GetStringBuilder().Clear();
        }

        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public async Task Completion_EmitsBashRegistrationAndEncodedCandidates()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        int dispatched = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddDirective(PredefinedDirectives.CreateCompletion("todo"))
            .AddChild(new CommandBuilder("add").SetHandler((_, _) => { dispatched++; return 0; })));
        CliHostOptions options = new(output, errors);

        int scriptCode = await CliInvoker.RunAsync(model, ["[completion:bash]"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, scriptCode);
        StringAssert.Contains(output.ToString(), "complete -F _mauve_completion_746F646F -- 'todo'", StringComparison.Ordinal);
        output.GetStringBuilder().Clear();
        int queryCode = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo ad", "todo ad"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, queryCode);
        Assert.AreEqual($"xYWRk{Environment.NewLine}", output.ToString());
        Assert.AreEqual(string.Empty, errors.ToString());
        Assert.AreEqual(0, dispatched);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public async Task Completion_RejectsUnsupportedModeAndSuffixWithoutOutput()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddDirective(PredefinedDirectives.CreateCompletion("todo"))
            .AddChild(new CommandBuilder("add")));
        CliHostOptions options = new(output, errors);

        int suffixCode = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo ad", "todo adxx"], options, TestContext.CancellationToken).ConfigureAwait(false);
        int shellCode = await CliInvoker.RunAsync(model, ["[completion:unknown]"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(2, suffixCode);
        Assert.AreEqual(2, shellCode);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(errors.ToString(), "Invalid completion", StringComparison.Ordinal);
        StringAssert.Contains(errors.ToString(), "Unsupported completion", StringComparison.Ordinal);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public void Completion_RejectsUnsafeCommandNames()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PredefinedDirectives.CreateCompletion("todo';touch"));
        Assert.ThrowsExactly<ArgumentException>(() => PredefinedDirectives.CreateCompletion("-todo"));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public async Task Completion_HandlesLogicalValuesAndRejectsShellExpansion()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(value, static (_, _) => Values("hello world", "$(touch bad)"))
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddDirective(PredefinedDirectives.CreateCompletion("todo", providers: providers))
            .AddOption(value));
        CliHostOptions options = new(output, errors);

        int bash = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo --value=h", "todo --value=h"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, bash);
        Assert.AreEqual($"xaGVsbG8gd29ybGQ={Environment.NewLine}", output.ToString());
        output.GetStringBuilder().Clear();
        int bashQuoted = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo --value=\"h", "todo --value=\"h"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, bashQuoted);
        Assert.AreEqual($"xaGVsbG8gd29ybGQ={Environment.NewLine}", output.ToString());
        output.GetStringBuilder().Clear();
        int bashEscaped = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo --value=hello\\ w", "todo --value=hello\\ w"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, bashEscaped);
        Assert.AreEqual($"xaGVsbG8gd29ybGQ={Environment.NewLine}", output.ToString());
        output.GetStringBuilder().Clear();
        int bashFull = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash-full", "todo --value=h", "todo --value=h"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, bashFull);
        Assert.AreEqual($"xLS12YWx1ZT1oZWxsbyB3b3JsZA=={Environment.NewLine}", output.ToString());
        output.GetStringBuilder().Clear();
        int fish = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "fish", "todo --value=h", "todo --value=h"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, fish);
        Assert.AreEqual($"xLS12YWx1ZT1oZWxsbyB3b3JsZA=={Environment.NewLine}", output.ToString());
        output.GetStringBuilder().Clear();
        int expansion = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo $(touch bad) ", "todo $(touch bad) "], options, TestContext.CancellationToken).ConfigureAwait(false);
        int quoted = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo --value=a\"h", "todo --value=a\"h"], options, TestContext.CancellationToken).ConfigureAwait(false);
        int escapedControl = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo --value=a\\\n", "todo --value=a\\\n"], options, TestContext.CancellationToken).ConfigureAwait(false);
        int colon = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo --value=a:b", "todo --value=a:b"], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(2, expansion);
        Assert.AreEqual(2, quoted);
        Assert.AreEqual(2, escapedControl);
        Assert.AreEqual(2, colon);
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public async Task Completion_PowerShellReplacesCompleteTokenAtMidWordCursor()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddDirective(PredefinedDirectives.CreateCompletion("todo"))
            .AddChild(new CommandBuilder("add")));
        int code = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "powershell", "todo ad", "todo adxx"],
            new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, code);
        Assert.AreEqual($"xYWRk{Environment.NewLine}", output.ToString());
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public async Task Completion_RejectsControlCharactersWithoutPartialOutput()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(value, static (_, _) => Values("valid", "bad\nvalue"))
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddDirective(PredefinedDirectives.CreateCompletion("todo", providers: providers))
            .AddOption(value));
        int code = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo --value=", "todo --value="],
            new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(2, code);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(errors.ToString(), "unsupported candidate", StringComparison.Ordinal);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public async Task Completion_ProviderFailureDoesNotEmitPartialCandidates()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(value, static (_, _) => FailingValues())
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddDirective(PredefinedDirectives.CreateCompletion("todo", providers: providers))
            .AddOption(value));
        int code = await CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", "bash", "todo --value=", "todo --value="],
            new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(2, code);
        Assert.AreEqual(string.Empty, output.ToString());
        StringAssert.Contains(errors.ToString(), "ProviderFailed", StringComparison.Ordinal);
    }

    private static async IAsyncEnumerable<CompletionValue> Values(params string[] values)
    {
        foreach (string value in values)
        {
            yield return new CompletionValue(value);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<CompletionValue> FailingValues()
    {
        yield return new CompletionValue("valid");
        await Task.Yield();
        throw new InvalidOperationException("A provider failed.");
    }
}
