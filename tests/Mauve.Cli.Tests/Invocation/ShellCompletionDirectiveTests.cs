using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
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
            Assert.Contains("todo", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("query-v1", output.ToString(), StringComparison.Ordinal);
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
        Assert.Contains("complete -F _mauve_completion_746F646F -- 'todo'", output.ToString(), StringComparison.Ordinal);
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
        Assert.Contains("Invalid completion", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unsupported completion", errors.ToString(), StringComparison.Ordinal);
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
        Assert.Contains("unsupported candidate", errors.ToString(), StringComparison.Ordinal);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public async Task Completion_ProviderFailureDoesNotEmitPartialCandidates()
    {
        foreach (string shell in new[] { "bash", "bash-full", "zsh", "fish", "powershell" })
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
                ["[completion:query-v1]", "--", shell, "todo --value=", "todo --value="],
                new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(2, code, shell);
            Assert.AreEqual(string.Empty, output.ToString(), shell);
            Assert.Contains("ProviderFailed", errors.ToString(), StringComparison.Ordinal);
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-008")]
    public async Task Completion_ShellProtocolTableValidatesInputAndDecodesInertRecords()
    {
        (string Shell, string Prefix, string Line, bool Accepted, string? ExpectedCandidate)[] cases =
        [
            ("bash", "todo --value=h", "todo --value=h", true, "hello world 🌿"),
            ("bash-full", "todo --value=h", "todo --value=h", true, "--value=hello world 🌿"),
            ("zsh", "todo --value=h", "todo --value=h", true, "--value=hello world 🌿"),
            ("fish", "todo --value=h", "todo --value=h", true, "--value=hello world 🌿"),
            ("powershell", "todo --value=h", "todo --value=h", true, "--value=hello world 🌿"),
            ("bash", "todo --value=\"h", "todo --value=\"h", true, "hello world 🌿"),
            ("bash-full", "todo --value=\"h", "todo --value=\"h", false, null),
            ("zsh", "todo --value=\"h", "todo --value=\"h", true, "--value=hello world 🌿"),
            ("fish", "todo --value=\"h", "todo --value=\"h", true, "--value=hello world 🌿"),
            ("powershell", "todo --value=\"h", "todo --value=\"h", true, "--value=hello world 🌿"),
            ("bash", "todo --value=\"h\"", "todo --value=\"h\"", false, null),
            ("bash-full", "todo --value=\"h\"", "todo --value=\"h\"", false, null),
            ("zsh", "todo --value=\"h\"", "todo --value=\"h\"", true, "--value=hello world 🌿"),
            ("fish", "todo --value=\"h\"", "todo --value=\"h\"", true, "--value=hello world 🌿"),
            ("powershell", "todo --value=\"h\"", "todo --value=\"h\"", true, "--value=hello world 🌿"),
            ("bash", "todo --value=hello\\ w", "todo --value=hello\\ w", true, "hello world 🌿"),
            ("bash-full", "todo --value=hello\\ w", "todo --value=hello\\ w", true, "--value=hello world 🌿"),
            ("zsh", "todo --value=hello\\ w", "todo --value=hello\\ w", true, "--value=hello world 🌿"),
            ("fish", "todo --value=hello\\ w", "todo --value=hello\\ w", true, "--value=hello world 🌿"),
            ("powershell", "todo --value=hello\\ w", "todo --value=hello\\ w", false, null),
            ("bash", "todo --value=h\"i", "todo --value=h\"i", false, null),
            ("bash", "todo --value=h\\\"i", "todo --value=h\\\"i", true, null),
            ("bash", "todo --value=\"h\\\"i", "todo --value=\"h\\\"i", false, null),
            ("bash-full", "todo --value=\"h\\\"i", "todo --value=\"h\\\"i", false, null),
            ("powershell", "todo --value='h''i", "todo --value='h''i", false, null),
            ("bash", "todo --value=$HOME", "todo --value=$HOME", false, null),
            ("zsh", "todo --value=`date`", "todo --value=`date`", false, null),
            ("fish", "todo --value=h;id", "todo --value=h;id", false, null),
            ("powershell", "todo --value=h|more", "todo --value=h|more", false, null),
            ("bash", "todo --value=h\n", "todo --value=h\n", false, null),
            ("zsh", "todo --value=h\0", "todo --value=h\0", false, null),
            ("fish", "todo --value=h🌿", "todo --value=h🌿", true, null),
            ("bash", "todo --value=h", "todo --value=h-tail", false, null),
            ("bash-full", "todo --value=h", "todo --value=h-tail", false, null),
            ("zsh", "todo --value=h", "todo --value=h-tail", false, null),
            ("fish", "todo --value=h", "todo --value=h-tail", false, null),
            ("powershell", "todo --value=h", "todo --value=h-tail", true, "--value=hello world 🌿"),
        ];

        foreach ((string shell, string prefix, string line, bool accepted, string? expectedCandidate) in cases)
        {
            using StringWriter output = new();
            using StringWriter errors = new();
            OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
            CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
                .Register(value, static (_, _) => Values("hello world 🌿"))
                .Build();
            CommandModel model = CommandModel.Create(new CommandBuilder("todo")
                .AddDirective(PredefinedDirectives.CreateCompletion("todo", providers: providers))
                .AddOption(value));

            int code = await CliInvoker.RunAsync(model,
                ["[completion:query-v1]", "--", shell, prefix, line],
                new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(accepted ? 0 : 2, code, $"{shell}: {line}");
            string[] records = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            if (expectedCandidate is null)
            {
                Assert.IsEmpty(records, $"{shell}: {line}");
            }
            else
            {
                Assert.HasCount(1, records, $"{shell}: {line}");
                Assert.IsTrue(records[0].StartsWith('x', StringComparison.Ordinal), $"{shell}: {line}");
                string decoded = new UTF8Encoding(false, true).GetString(
                    Convert.FromBase64String(records[0][1..]));
                Assert.AreEqual(expectedCandidate, decoded, $"{shell}: {line}");
            }

            if (!accepted)
            {
                Assert.IsEmpty(output.ToString(), $"{shell}: {line}");
                Assert.Contains("Invalid completion", errors.ToString(), StringComparison.Ordinal);
            }
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-008")]
    public async Task Completion_RejectsShellSyntaxAndControlsButKeepsUnpairedUtf16Inert()
    {
        string[] shells = ["bash", "bash-full", "zsh", "fish", "powershell"];
        (string Text, bool Rejected)[] inputs =
        [
            ("$HOME", true),
            ("`date`", true),
            (";id", true),
            ("|more", true),
            ("&id", true),
            ("<file", true),
            (">file", true),
            ("line\nbreak", true),
            ("nul\0byte", true),
            ("tab\tbyte", true),
            ("\ud800", false),
            ("\udc00", false),
            ("\ud800x", false),
        ];

        foreach (string shell in shells)
        {
            foreach ((string text, bool rejected) in inputs)
            {
                using StringWriter output = new();
                using StringWriter errors = new();
                OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
                CommandModel model = CommandModel.Create(new CommandBuilder("todo")
                    .AddDirective(PredefinedDirectives.CreateCompletion("todo"))
                    .AddOption(value));
                string payload = "todo --value=" + text;

                int code = await CliInvoker.RunAsync(model,
                    ["[completion:query-v1]", "--", shell, payload, payload],
                    new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

                string caseLabel = $"{shell}, input U+{(int)text[^1]:X4}";
                Assert.AreEqual(rejected ? 2 : 0, code, caseLabel);
                Assert.AreEqual(string.Empty, output.ToString(), caseLabel);
                if (rejected)
                {
                    Assert.Contains("Invalid completion", errors.ToString(), StringComparison.Ordinal);
                }
                else
                {
                    Assert.AreEqual(string.Empty, errors.ToString(), caseLabel);
                }
            }
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-008")]
    public async Task Completion_EncodesShellMetacharactersAsInertStrictUtf8Records()
    {
        string[] shells = ["bash", "bash-full", "zsh", "fish", "powershell"];
        string[] candidates = ["$HOME", "`date`", @"C:\temp", "\"double quoted\"", "'single quoted'", "$(touch bad)"];

        foreach (string shell in shells)
        {
            using StringWriter output = new();
            using StringWriter errors = new();
            OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
            CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
                .Register(value, (_, _) => Values(candidates))
                .Build();
            CommandModel model = CommandModel.Create(new CommandBuilder("todo")
                .AddDirective(PredefinedDirectives.CreateCompletion("todo", providers: providers))
                .AddOption(value));

            int code = await CliInvoker.RunAsync(model,
                ["[completion:query-v1]", "--", shell, "todo --value=", "todo --value="],
                new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(0, code, shell);
            string[] records = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.HasCount(candidates.Length, records, shell);
            for (int index = 0; index < records.Length; index++)
            {
                Assert.IsTrue(records[index].StartsWith('x', StringComparison.Ordinal), shell);
                string decoded = new UTF8Encoding(false, true).GetString(
                    Convert.FromBase64String(records[index][1..]));
                string expected = shell == "bash" ? candidates[index] : "--value=" + candidates[index];
                Assert.AreEqual(expected, decoded, $"{shell}, record {index}");
            }

            Assert.AreEqual(string.Empty, errors.ToString(), shell);
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-008")]
    public async Task Completion_MalformedRequestsAndUnsafeCandidatesNeverWritePartialRecords()
    {
        string[][] malformedRequests =
        [
            ["[completion:query-v1]", "--", "bash", "todo --value=h"],
            ["[completion:query-v1]", "--", "bash", "todo --value=h", "todo --value=h", "extra"],
            ["[completion:query-v1]", "--", "bash", "not-todo", "todo --value=h"],
        ];
        foreach (string[] arguments in malformedRequests)
        {
            using StringWriter output = new();
            using StringWriter errors = new();
            CommandModel model = CommandModel.Create(new CommandBuilder("todo")
                .AddDirective(PredefinedDirectives.CreateCompletion("todo")));

            int code = await CliInvoker.RunAsync(model, arguments,
                new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(2, code);
            Assert.AreEqual(string.Empty, output.ToString());
            Assert.Contains("Invalid completion", errors.ToString(), StringComparison.Ordinal);
        }

        string[] unsafeCandidates = ["bad\nvalue", "bad\0value", "bad\ud800value", "bad\udc00value"];
        foreach (string shell in new[] { "bash", "bash-full", "zsh", "fish", "powershell" })
        {
            foreach (string unsafeCandidate in unsafeCandidates)
            {
                using StringWriter output = new();
                using StringWriter errors = new();
                OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
                CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
                    .Register(value, (_, _) => Values("valid", unsafeCandidate))
                    .Build();
                CommandModel model = CommandModel.Create(new CommandBuilder("todo")
                    .AddDirective(PredefinedDirectives.CreateCompletion("todo", providers: providers))
                    .AddOption(value));

                int code = await CliInvoker.RunAsync(model,
                    ["[completion:query-v1]", "--", shell, "todo --value=", "todo --value="],
                    new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);

                string caseLabel = $"{shell}, candidate U+{(int)unsafeCandidate[^1]:X4}";
                Assert.AreEqual(2, code, caseLabel);
                Assert.AreEqual(string.Empty, output.ToString(), caseLabel);
                Assert.Contains("unsupported candidate", errors.ToString(), StringComparison.Ordinal);
                Assert.IsFalse(errors.ToString().Contains(unsafeCandidate, StringComparison.Ordinal));
            }
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-008")]
    public async Task Completion_CancellationAfterCandidateEnumerationWritesNoProtocolRecords()
    {
        foreach (string shell in new[] { "bash", "bash-full", "zsh", "fish", "powershell" })
        {
            using StringWriter output = new();
            using StringWriter errors = new();
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource providerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
            CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
                .Register(value, (_, token) => WaitingValues(providerStarted, token))
                .Build();
            CommandModel model = CommandModel.Create(new CommandBuilder("todo")
                .AddDirective(PredefinedDirectives.CreateCompletion("todo", providers: providers))
                .AddOption(value));

            ValueTask<InvocationResult> pending = new CliInvoker().InvokeAsync(model,
                ["[completion:query-v1]", "--", shell, "todo --value=", "todo --value="],
                new CliHostOptions(output, errors), cancellation.Token);
            await providerStarted.Task.ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            InvocationResult result = await pending.ConfigureAwait(false);

            Assert.AreEqual(InvocationOutcome.Cancelled, result.Outcome, shell);
            Assert.AreEqual(string.Empty, output.ToString(), shell);
        }
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

    private static async IAsyncEnumerable<CompletionValue> WaitingValues(
        TaskCompletionSource providerStarted,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new CompletionValue("valid");
        providerStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        yield return new CompletionValue("unreachable");
    }
}
