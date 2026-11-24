using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class MulticallDispatchTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-17")]
    [TestProperty(Metadata.Requirement, "FEAT-17-001")]
    public void Route_SnapshotsPathsAndRejectsMalformedConfiguration()
    {
        string[] path = ["tool", "copy"];
        CliMulticallRoute route = new("smurf", path);
        path[1] = "missing";
        Assert.AreSequenceEqual(["tool", "copy"], route.CommandPath);
        Assert.ThrowsExactly<ArgumentNullException>(() => new CliMulticallRoute("smurf", null!));
        Assert.ThrowsExactly<ArgumentException>(() => new CliMulticallRoute("smurf", []));
        Assert.ThrowsExactly<ArgumentException>(() => new CliMulticallRoute("smurf", ["tool", " "]));
        Assert.ThrowsExactly<ArgumentNullException>(() => new CliMulticallRoute("smurf", ["tool", null!]));
        foreach (string name in new[] { "", " ", ".", "..", "bin/smurf", @"bin\smurf", "smurf\n" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new CliMulticallRoute(name, ["tool"]));
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-17")]
    [TestProperty(Metadata.Requirement, "FEAT-17-002")]
    public async Task Dispatch_SelectsImplicitRootsAndPlatformNormalizedExecutableNames()
    {
        CommandModel first = CommandModel.Create(new CommandBuilder("first")
            .AddAlias("one").SetHandler(static (_, _) => 11));
        CommandModel second = CommandModel.Create(new CommandBuilder("second")
            .SetHandler(static (_, _) => 22));
        IReadOnlyList<CommandModel> models = [first, second];
        foreach ((string name, int expected) in new[] { ("first", 11), ("one", 11), ("second", 22) })
        {
            int code = await CliInvoker.DispatchMulticallAsync(models, arguments: [],
                options: new CliHostOptions(executableName: Path.Combine("bin", name)),
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(expected, code);
        }

        CliHostOptions normalized = new(executableName: Path.Combine("bin", "ONE.EXE"));
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual(11, await CliInvoker.DispatchMulticallAsync(models, arguments: [],
                options: normalized, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
        }
        else
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CliInvoker.DispatchMulticallAsync(
                models, arguments: [], options: normalized, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
            CommandModel extension = CommandModel.Create(new CommandBuilder("one.exe").SetHandler(static (_, _) => 33));
            Assert.AreEqual(33, await CliInvoker.DispatchMulticallAsync([extension], arguments: [],
                options: new CliHostOptions(executableName: "one.exe"),
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-17")]
    [TestProperty(Metadata.Requirement, "FEAT-17-003")]
    public async Task Dispatch_PreservesCanonicalAncestryArgumentsDirectivesAndOptionScope()
    {
        List<string> calls = [];
        ParseResult? captured = null;
        OptionDefinition inherited = new("workspace", typeof(string), OptionArity.RequiredScalar,
            scope: OptionScope.Inherited, isRequired: true);
        OptionDefinition rootLocal = new("root-only", typeof(bool), OptionArity.BooleanFlag);
        OptionDefinition count = new("count", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        CommandBuilder leaf = new CommandBuilder("deeper").AddAlias("deep")
            .SetHandler((context, _) =>
            {
                captured = context.ParseResult;
                calls.Add("handler");
                return 12;
            });
        CommandBuilder copy = new CommandBuilder("copy").AddAlias("cp")
            .AddOption(count).AddChild(leaf)
            .SetHandler((context, _) =>
            {
                captured = context.ParseResult;
                calls.Add("handler");
                Assert.IsTrue(context.TryGetOptionValue(inherited, out string? workspace));
                Assert.IsTrue(context.TryGetOptionValue(count, out int value));
                Assert.AreEqual("env-workspace", workspace);
                return value;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddAlias("suite")
            .RegisterConverter(static (text, _) => text == "three"
                ? ValueConversion.FromValue(3)
                : ValueConversion.Failure<int>("count.invalid"))
            .AddOption(inherited).AddOption(rootLocal).AddChild(copy)
            .AddDirective(new DirectiveBuilder("notice").SetHandler((context, _) =>
            {
                calls.Add("directive");
                Assert.AreEqual("copy", context.CommandContext.Command.Name);
                return DirectiveResult.ContinueProcessing;
            })));
        IReadOnlyList<CliMulticallRoute> routes = [new("smurf", ["suite", "cp"])];
        using StringWriter errors = new();
        CliHostOptions options = new(standardError: errors, executableName: "smurf",
            enableEnvironmentVariables: true,
            environmentVariableLookup: static name => name == "TOOL_WORKSPACE" ? "env-workspace" : null);
        string[] arguments = ["[notice]", "--count", "three"];
        Assert.AreEqual(3, await CliInvoker.DispatchMulticallAsync([model], routes, arguments, options,
            TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreSequenceEqual(["directive", "handler"], calls);
        Assert.AreSequenceEqual(arguments, captured!.Arguments);
        Assert.AreSequenceEqual(["tool", "copy"], [.. captured.SelectedPath.Select(static command => command.Name)]);
        Assert.AreSequenceEqual([1, 2], captured.Values.Single(value => ReferenceEquals(value.Option, count)).TokenIndices);

        calls.Clear();
        Assert.AreEqual(12, await CliInvoker.DispatchMulticallAsync([model], routes, ["deep"], options,
            TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreSequenceEqual(["tool", "copy", "deeper"], [.. captured.SelectedPath.Select(static command => command.Name)]);
        Assert.AreSequenceEqual(["deep"], captured.Arguments);

        calls.Clear();
        Assert.AreEqual(2, await CliInvoker.DispatchMulticallAsync([model], routes, ["--root-only"], options,
            TestContext.CancellationToken).ConfigureAwait(false));
        Assert.IsEmpty(calls);
        StringAssert.Contains(errors.ToString(), "Argument 1:", StringComparison.Ordinal);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-17")]
    [TestProperty(Metadata.Requirement, "FEAT-17-003")]
    public async Task Dispatch_RendersMappedHelpWithoutRepeatingItsCommandPrefix()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy")
                .AddExample("$0 copy --count 3")
                .AddChild(new CommandBuilder("deeper").AddExample("$0 copy deeper"))),
            includeStandardHelp: true);
        using StringWriter output = new();
        using StringWriter errors = new();
        CliHostOptions options = new(output, errors, executableName: "smurf",
            helpRenderer: static (document, _) => document.Usage + "\n" + string.Join('\n', document.Examples));
        IReadOnlyList<CliMulticallRoute> routes = [new("smurf", ["tool", "copy"])];
        Assert.AreEqual(0, await CliInvoker.DispatchMulticallAsync([model], routes, ["--help"], options,
            TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual("smurf [OPTIONS]\nsmurf --count 3" + Environment.NewLine, output.ToString());
        output.GetStringBuilder().Clear();
        Assert.AreEqual(0, await CliInvoker.DispatchMulticallAsync([model], routes, ["deeper", "--help"], options,
            TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual("smurf deeper [OPTIONS]\nsmurf deeper" + Environment.NewLine, output.ToString());
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-17")]
    [TestProperty(Metadata.Requirement, "FEAT-17-003")]
    public async Task Dispatch_PreservesCancellationExceptionsAndInvalidInputCoordinates()
    {
        int calls = 0;
        OptionDefinition count = new("count", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy").AddOption(count).SetTaskHandlerWithExitCode(async (_, _) =>
            {
                calls++;
                await Task.Yield();
                throw new IOException("private");
            })));
        using StringWriter errors = new();
        CliHostOptions options = new CliHostOptionsBuilder()
            .WithExecutableName("smurf").WithStandardError(errors)
            .MapException<IOException>(74, "Safe error.")
            .Build();
        IReadOnlyList<CliMulticallRoute> routes = [new("smurf", ["tool", "copy"])];
        Assert.AreEqual(2, await CliInvoker.DispatchMulticallAsync([model], routes, ["--count", "invalid"], options,
            TestContext.CancellationToken).ConfigureAwait(false));
        StringAssert.StartsWith(errors.ToString(), "Argument 2:", StringComparison.Ordinal);
        Assert.AreEqual(0, calls);
        errors.GetStringBuilder().Clear();
        Assert.AreEqual(74, await CliInvoker.DispatchMulticallAsync([model], routes, ["--count", "3"], options,
            TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual("Safe error." + Environment.NewLine, errors.ToString());
        Assert.AreEqual(1, calls);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync().ConfigureAwait(false);
        Assert.AreEqual(130, await CliInvoker.DispatchMulticallAsync([model], routes, ["--count", "3"], options,
            cancelled.Token).ConfigureAwait(false));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-17")]
    [TestProperty(Metadata.Requirement, "FEAT-17-001")]
    [TestProperty(Metadata.Requirement, "FEAT-17-002")]
    public async Task Dispatch_RejectsInvalidOrConflictingRoutesBeforeCallbacks()
    {
        int calls = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddAlias("suite")
            .SetHandler((_, _) => ++calls)
            .AddDirective(new DirectiveBuilder("notice").SetHandler((_, _) => ++calls))
            .AddChild(new CommandBuilder("copy").SetHandler((_, _) => ++calls)));
        CliHostOptions options = new CliHostOptionsBuilder().WithExecutableName("tool")
            .MapException<Exception>(1, "Should not map setup errors.").Build();
        foreach (IReadOnlyList<CliMulticallRoute> routes in new IReadOnlyList<CliMulticallRoute>[]
        {
            [new("smurf", ["missing"])],
            [new("smurf", ["tool", "missing"])],
            [new("smurf", ["TOOL", "copy"])],
            [new("smurf", ["tool", "COPY"])],
            [new("suite", ["tool", "copy"])],
            [new("smurf", ["tool"]), new("smurf", ["tool", "copy"])]
        })
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => CliInvoker.DispatchMulticallAsync(
                [model], routes, ["[notice]"], options, TestContext.CancellationToken)).ConfigureAwait(false);
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => CliInvoker.DispatchMulticallAsync(
            [], arguments: [], options: options, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => CliInvoker.DispatchMulticallAsync(
            null!, arguments: [], options: options, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => CliInvoker.DispatchMulticallAsync(
            [null!], arguments: [], options: options, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => CliInvoker.DispatchMulticallAsync(
            [model], [null!], [], options, TestContext.CancellationToken)).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => CliInvoker.DispatchMulticallAsync(
            [model, model], arguments: [], options: options, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CliInvoker.DispatchMulticallAsync(
            [model], arguments: ["[notice]"], options: new CliHostOptions(executableName: "missing"),
            cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        if (OperatingSystem.IsWindows())
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => CliInvoker.DispatchMulticallAsync(
                [model], [new("TOOL.EXE", ["tool", "copy"])], [], options, TestContext.CancellationToken)).ConfigureAwait(false);
        }

        Assert.AreEqual(0, calls);
    }
}
