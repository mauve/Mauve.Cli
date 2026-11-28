using System;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class HostDiagnosticRedactionTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-003")]
    public async Task RunAsync_DefaultHostIncludesEscapedUnexpectedArgument()
    {
        CommandModel model = CreateModel();
        using StringWriter errors = new();
        CliHostOptions options = new(standardError: errors);

        int exitCode = await CliInvoker.RunAsync(model, ["first", "extra\nline"], options, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.IsTrue(options.IncludeArgumentTextInDiagnostics);
        Assert.AreEqual(2, exitCode);
        Assert.AreEqual("Argument 2: Unexpected positional argument \"extra\\nline\"." + Environment.NewLine, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-003")]
    public async Task RunAsync_SuppressesUnexpectedArgumentWithoutChangingParseResults()
    {
        const string sentinel = "secret-like-sentinel";
        int handlerCalls = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("name", typeof(string)))
            .SetHandler((_, _) => handlerCalls++));
        using StringWriter errors = new();
        CliHostOptions options = new(includeArgumentTextInDiagnostics: false, standardError: errors);

        int asyncExit = await CliInvoker.RunAsync(model, ["first", sentinel], options, TestContext.CancellationToken)
            .ConfigureAwait(false);
        InvocationResult result = await new CliInvoker().InvokeAsync(model, ["first", sentinel], options, TestContext.CancellationToken)
            .ConfigureAwait(false);

        string expected = "Argument 2: Unexpected positional argument." + Environment.NewLine;
        Assert.AreEqual(2, asyncExit);
        Assert.AreEqual(expected, errors.ToString());
        Assert.IsFalse(errors.ToString().Contains(sentinel, StringComparison.Ordinal));
        Assert.AreEqual(0, handlerCalls);
        Assert.AreEqual(InvocationOutcome.InvalidInput, result.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.UnexpectedArgument, result.ParseResult!.Diagnostics[0].Code);
        Assert.AreEqual(sentinel, result.ParseResult.UnmatchedTokens[0].Value);
        Assert.AreEqual($"Argument 2: Unexpected positional argument \"{sentinel}\".", result.ParseResult.Diagnostics[0].ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-003")]
    public void Run_SuppressesUnexpectedArgument()
    {
        const string sentinel = "secret-like-sentinel";
        int handlerCalls = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("name", typeof(string)))
            .SetHandler((_, _) => handlerCalls++));
        using StringWriter errors = new();
        CliHostOptions options = new(includeArgumentTextInDiagnostics: false, standardError: errors);

        int exitCode = CliInvoker.Run(model, ["first", sentinel], options, TestContext.CancellationToken);

        Assert.AreEqual(2, exitCode);
        Assert.AreEqual("Argument 2: Unexpected positional argument." + Environment.NewLine, errors.ToString());
        Assert.IsFalse(errors.ToString().Contains(sentinel, StringComparison.Ordinal));
        Assert.AreEqual(0, handlerCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-003")]
    public void ConstructorAndBuilder_PreserveEquivalentIndependentPolicySnapshots()
    {
        CommandModel model = CreateModel();
        using StringWriter errors = new();
        CliHostOptionsBuilder builder = new CliHostOptionsBuilder().WithStandardError(errors);
        CliHostOptions defaultSnapshot = builder.Build();
        CliHostOptions redactedSnapshot = builder.WithArgumentTextInDiagnostics(false).Build();
        CliHostOptions includedSnapshot = builder.WithArgumentTextInDiagnostics(true).Build();
        CliHostOptions constructorRedacted = new(includeArgumentTextInDiagnostics: false, standardError: errors);
        CliHostOptions constructorIncluded = new(includeArgumentTextInDiagnostics: true, standardError: errors);

        Assert.IsTrue(defaultSnapshot.IncludeArgumentTextInDiagnostics);
        Assert.IsFalse(redactedSnapshot.IncludeArgumentTextInDiagnostics);
        Assert.IsTrue(includedSnapshot.IncludeArgumentTextInDiagnostics);
        Assert.IsFalse(constructorRedacted.IncludeArgumentTextInDiagnostics);
        Assert.IsTrue(constructorIncluded.IncludeArgumentTextInDiagnostics);

        string included = "Argument 2: Unexpected positional argument \"secret\"." + Environment.NewLine;
        string redacted = "Argument 2: Unexpected positional argument." + Environment.NewLine;
        foreach (CliHostOptions options in new[] { defaultSnapshot, redactedSnapshot, includedSnapshot, constructorRedacted, constructorIncluded })
        {
            Assert.AreEqual(2, CliInvoker.Run(model, ["first", "secret"], options, TestContext.CancellationToken));
        }

        Assert.AreEqual(included + redacted + included + redacted + included, errors.ToString());
    }

    private static CommandModel CreateModel() => CommandModel.Create(new CommandBuilder("tool")
        .AddPositional(new PositionalDefinition("name", typeof(string))));
}
