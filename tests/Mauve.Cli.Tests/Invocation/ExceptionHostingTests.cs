using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class ExceptionHostingTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    public void ExceptionMappings_TryResolveSelectsExactAndMostSpecificAssignableTypeRegardlessOfOrder()
    {
        foreach (bool specificFirst in new[] { false, true })
        {
            ExceptionMappingRegistryBuilder builder = new();
            if (specificFirst)
            {
                builder.Map<FileNotFoundException>(74, "Exact.");
                builder.Map<IOException>(2, "I/O.");
                builder.Map<Exception>(1, "General.");
            }
            else
            {
                builder.Map<Exception>(1, "General.");
                builder.Map<IOException>(2, "I/O.");
                builder.Map<FileNotFoundException>(74, "Exact.");
            }

            ExceptionMappingRegistry registry = builder.Build();
            Assert.IsTrue(registry.TryResolve(new FileNotFoundException("secret"), out CliExceptionResult? exact));
            Assert.AreEqual(74, exact.ExitCode);
            Assert.AreEqual("Exact.", exact.SafeMessage);
            Assert.IsTrue(registry.TryResolve(new DirectoryNotFoundException("secret"), out CliExceptionResult? derived));
            Assert.AreEqual(2, derived.ExitCode);
            Assert.AreEqual("I/O.", derived.SafeMessage);
            Assert.IsTrue(registry.TryResolve(new InvalidOperationException("secret"), out CliExceptionResult? baseMapping));
            Assert.AreEqual(1, baseMapping.ExitCode);
            Assert.AreEqual("General.", baseMapping.SafeMessage);
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    public void ExceptionMappings_TryResolveReportsUnmatchedAndPropagatesNullAndMapperFaults()
    {
        Assert.IsFalse(ExceptionMappingRegistry.Empty().TryResolve(new CliExitCodeException(74, "Safe."), out CliExceptionResult? unmatched));
        Assert.IsNull(unmatched);
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            ExceptionMappingRegistry.Empty().TryResolve(null!, out _));

        InvalidOperationException mapperFailure = new("Mapper failure.");
        ExceptionMappingRegistry registry = new ExceptionMappingRegistryBuilder()
            .Map<Exception>(1, _ => throw mapperFailure)
            .Build();
        InvalidOperationException actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
            registry.TryResolve(new IOException("secret"), out _));
        Assert.AreSame(mapperFailure, actual);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    [TestProperty(Metadata.Requirement, "FEAT-09-007")]
    [SuppressMessage("Usage", "CA1849:Call async methods when in an async method", Justification = "Verifies synchronous and asynchronous host parity.")]
    public async Task Run_HandlesDirectExceptionsAcrossGeneratedAndExplicitHosts()
    {
        using StringWriter errors = new();
        CliHostOptions options = new(standardError: errors);
        CliExitCodeException failure = new(74, "Safe failure.", new InvalidOperationException("secret"));
        CommandModel model = CreateFaultModel(failure);

        Assert.AreEqual(74, CliInvoker.Run(model, [], options, TestContext.CancellationToken));
        Assert.AreEqual(74, await CliInvoker.RunAsync(model, [], options, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(74, CliInvoker.Run<ExceptionHostCommand>([], options, TestContext.CancellationToken));
        Assert.AreEqual(74, await CliInvoker.RunAsync<ExceptionHostCommand>([], options, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(74, await CliInvoker.RunAsync<AsyncExceptionHostCommand>([], options, TestContext.CancellationToken).ConfigureAwait(false));
        CommandModel asyncModel = CommandModel.Create(new CommandBuilder("async-fault")
            .SetTaskHandlerWithExitCode(async (_, _) =>
            {
                await Task.Yield();
                throw failure;
            }));
        Assert.AreEqual(74, await CliInvoker.RunAsync(asyncModel, [], options, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(string.Concat(System.Linq.Enumerable.Repeat("Safe failure." + Environment.NewLine, 6)), errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    [SuppressMessage("Usage", "CA1849:Call async methods when in an async method", Justification = "Verifies synchronous and asynchronous mapping parity.")]
    public async Task Run_ChoosesMostSpecificMappingAndExplicitOverrides()
    {
        foreach (bool derivedFirst in new[] { false, true })
        {
            ExceptionMappingRegistryBuilder builder = new();
            if (derivedFirst)
            {
                builder.Map<CliExitCodeException>(77, static exception => "Mapped: " + exception.SafeMessage);
            }

            builder.Map<Exception>(1, "General failure.");
            if (!derivedFirst)
            {
                builder.Map<CliExitCodeException>(77, static exception => "Mapped: " + exception.SafeMessage);
            }

            using StringWriter errors = new();
            CliHostOptions options = new(standardError: errors, exceptionMappings: builder.Build());
            Assert.AreEqual(77, CliInvoker.Run<ExceptionHostCommand>([], options, TestContext.CancellationToken));
            Assert.AreEqual(77, await CliInvoker.RunAsync<AsyncExceptionHostCommand>([], options, TestContext.CancellationToken).ConfigureAwait(false));
            Assert.AreEqual("Mapped: Safe failure." + Environment.NewLine + "Mapped: Safe failure." + Environment.NewLine, errors.ToString());
        }

        using StringWriter derivedErrors = new();
        CliHostOptions derived = new(standardError: derivedErrors, exceptionMappings: new ExceptionMappingRegistryBuilder()
            .Map<Exception>(1, "General.")
            .Map<IOException>(74, "I/O failure.")
            .Build());
        Assert.AreEqual(74, CliInvoker.Run(CreateFaultModel(new FileNotFoundException("secret")), [], derived, TestContext.CancellationToken));
        Assert.AreEqual("I/O failure." + Environment.NewLine, derivedErrors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    public async Task InvokeAsync_PreservesFaultsAndCancellationDespiteHostMappings()
    {
        using StringWriter errors = new();
        CliHostOptions options = new CliHostOptionsBuilder()
            .WithStandardError(errors)
            .WithExitCodes(cancelled: 99)
            .MapException<Exception>(7, "Failure.")
            .Build();
        CliExitCodeException direct = new(74, "Safe failure.");
        CommandModel model = CreateFaultModel(direct);
        CliExitCodeException actual = await Assert.ThrowsExactlyAsync<CliExitCodeException>(async () =>
            await new CliInvoker().InvokeAsync(model, [], options, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.AreSame(direct, actual);
        Assert.AreEqual(string.Empty, errors.ToString());

        using CancellationTokenSource source = new();
        CommandModel cancelling = CommandModel.Create(new CommandBuilder("cancel")
            .SetTaskHandler(async (_, token) =>
            {
                await source.CancelAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }));
        Assert.AreEqual(99, await CliInvoker.RunAsync(cancelling, [], options, source.Token).ConfigureAwait(false));
        Assert.AreEqual(string.Empty, errors.ToString());

        OperationCanceledException unrelated = new("Unrelated.");
        Assert.AreEqual(7, await CliInvoker.RunAsync(CreateFaultModel(unrelated), [], options, TestContext.CancellationToken).ConfigureAwait(false));
        InvalidOperationException unmapped = new("Unmapped.");
        InvalidOperationException hostFault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await CliInvoker.RunAsync(CreateFaultModel(unmapped), [], cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.AreSame(unmapped, hostFault);
        InvalidOperationException syncFault = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CliInvoker.Run(CreateFaultModel(unmapped), [], cancellationToken: TestContext.CancellationToken));
        Assert.AreSame(unmapped, syncFault);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    public async Task Run_MapsInvocationCallbacksButDoesNotRemapMappingFaults()
    {
        InvalidOperationException failure = new("Callback secret.");
        CommandModel model = CommandModel.Create(new CommandBuilder("callback")
            .AddOption(new OptionDefinition("value", typeof(string), OptionArity.RequiredScalar,
                defaultValuePolicy: DefaultValuePolicy.Factory, defaultValueFactory: () => throw failure)));
        using StringWriter errors = new();
        CliHostOptions options = new CliHostOptionsBuilder()
            .WithStandardError(errors)
            .MapException<InvalidOperationException>(9, "Callback failed.")
            .Build();
        Assert.AreEqual(9, await CliInvoker.RunAsync(model, [], options, TestContext.CancellationToken).ConfigureAwait(false));

        CliHostOptions badMapper = new CliHostOptionsBuilder()
            .MapException<Exception>(1, _ => throw failure)
            .Build();
        InvalidOperationException actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CliInvoker.Run<ExceptionHostCommand>([], badMapper, TestContext.CancellationToken));
        Assert.AreSame(failure, actual);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    [SuppressMessage("Usage", "CA1849:Call async methods when in an async method", Justification = "Verifies both synchronous and asynchronous additional-directive overloads.")]
    public async Task Run_MapsAdditionalDirectivesWithoutDispatchingCommandsOrCatchingSetupFaults()
    {
        int commandCalls = 0;
        int laterDirectiveCalls = 0;
        using StringWriter errors = new();
        CliHostOptions options = new CliHostOptionsBuilder().WithStandardError(errors)
            .MapException<Exception>(9, "Directive failure.").Build();
        DirectiveBuilder directive = new DirectiveBuilder("fault")
            .SetHandler(static int (_, _) => throw new IOException("Private directive failure."));
        DirectiveBuilder later = new DirectiveBuilder("later").SetHandler((_, _) => laterDirectiveCalls++);
        CommandModel model = CommandModel.Create(new CommandBuilder("directive-fault")
            .AddDirective(directive)
            .AddDirective(later)
            .SetHandler((_, _) => commandCalls++));
        Assert.AreEqual(9, await CliInvoker.RunAsync(model, ["[fault]", "[later]"], options, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(9, CliInvoker.Run<ExceptionHostCommand>(["[fault]", "[later]"], [directive, later], options, TestContext.CancellationToken));
        Assert.AreEqual(9, await CliInvoker.RunAsync<AsyncExceptionHostCommand>(["[fault]", "[later]"], [directive, later], options, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(0, commandCalls);
        Assert.AreEqual(0, laterDirectiveCalls);
        Assert.ThrowsExactly<ArgumentException>(() => CliInvoker.Run<ExceptionHostCommand>(
            [], [directive, directive], options, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    public async Task Run_DoesNotMapAnInnerExceptionOfAnUnmappedOuterFault()
    {
        InvalidOperationException outer = new("Outer.", new IOException("Inner."));
        using StringWriter errors = new();
        CliHostOptions options = new CliHostOptionsBuilder().WithStandardError(errors)
            .MapException<IOException>(9, "I/O failure.").Build();
        InvalidOperationException actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await CliInvoker.RunAsync(CreateFaultModel(outer), [], options, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.AreSame(outer, actual);
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-007")]
    [SuppressMessage("Usage", "CA1849:Call async methods when in an async method", Justification = "Verifies rendering in both synchronous and asynchronous hosts.")]
    public async Task Run_UsesRenderingContextAndRejectsBrokenRenderersWithoutRemapping()
    {
        using StringWriter errors = new();
        CliExitCodeException failure = new(74, "Safe failure.");
        int rendererCalls = 0;
        CliHostOptions options = new CliHostOptionsBuilder()
            .WithStandardError(errors)
            .WithExecutableName("test-cli")
            .WithExceptionRenderer(context =>
            {
                rendererCalls++;
                Assert.AreSame(failure, context.Exception);
                Assert.AreEqual(74, context.Result.ExitCode);
                return context.ExecutableName + ": " + context.Result.SafeMessage;
            })
            .Build();
        Assert.AreEqual(74, await CliInvoker.RunAsync(CreateFaultModel(failure), [], options, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(1, rendererCalls);
        Assert.AreEqual("test-cli: Safe failure." + Environment.NewLine, errors.ToString());

        CliHostOptions suppressed = new(standardError: errors, exceptionRenderer: static _ => string.Empty);
        errors.GetStringBuilder().Clear();
        Assert.AreEqual(74, CliInvoker.Run(CreateFaultModel(failure), [], suppressed, TestContext.CancellationToken));
        Assert.AreEqual(string.Empty, errors.ToString());

        CliHostOptions nullRenderer = new(exceptionRenderer: static _ => null!);
        Assert.ThrowsExactly<InvalidOperationException>(() => CliInvoker.Run(CreateFaultModel(failure), [], nullRenderer, TestContext.CancellationToken));
        InvalidOperationException rendererFault = new("Renderer failure.");
        CliHostOptions throwing = new CliHostOptionsBuilder()
            .MapException<Exception>(1, "Catch-all.")
            .WithExceptionRenderer(_ => throw rendererFault)
            .Build();
        InvalidOperationException actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await CliInvoker.RunAsync(CreateFaultModel(failure), [], throwing, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.AreSame(rendererFault, actual);

        IOException writerFailure = new("Writer failure.");
        using ThrowingWriter writer = new(writerFailure);
        CliHostOptions brokenWriter = new(standardError: writer, exceptionMappings: throwing.ExceptionMappings);
        IOException actualWriterFailure = Assert.ThrowsExactly<IOException>(() =>
            CliInvoker.Run(CreateFaultModel(failure), [], brokenWriter, TestContext.CancellationToken));
        Assert.AreSame(writerFailure, actualWriterFailure);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    [TestProperty(Metadata.Requirement, "FEAT-09-008")]
    public void Builder_RejectsInvalidMappingsAndKeepsCompositionAtomic()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CliExitCodeException(0, "Failure."));
        Assert.ThrowsExactly<ArgumentException>(() => new CliExitCodeException(1, " "));
        ExceptionMappingRegistryBuilder mappings = new ExceptionMappingRegistryBuilder().Map<IOException>(7, "I/O.");
        Assert.ThrowsExactly<InvalidOperationException>(() => mappings.Map<IOException>(8, "Duplicate."));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => mappings.Map<Exception>(0, static _ => "Failure."));
        Assert.ThrowsExactly<ArgumentNullException>(() => mappings.Map(1, (Func<Exception, string>)null!));
        ExceptionMappingRegistry additions = new ExceptionMappingRegistryBuilder()
            .Map<InvalidOperationException>(8, "Other.")
            .Map<IOException>(9, "Duplicate.")
            .Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => mappings.Add(additions));
        mappings.Map<InvalidOperationException>(8, "Other.");

        CliHostOptionsBuilder hostBuilder = new CliHostOptionsBuilder().MapException<IOException>(7, "I/O.");
        CliHostOptions beforeRejectedAdd = hostBuilder.Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => hostBuilder.AddExceptionMappings(additions));
        CliHostOptions afterRejectedAdd = hostBuilder.Build();
        foreach (CliHostOptions snapshot in new[] { beforeRejectedAdd, afterRejectedAdd })
        {
            Assert.IsTrue(snapshot.ExceptionMappings.TryResolve(new IOException(), out CliExceptionResult? mapped));
            Assert.AreEqual(7, mapped.ExitCode);
            Assert.IsFalse(snapshot.ExceptionMappings.TryResolve(new InvalidOperationException(), out _));
        }

        hostBuilder.MapException<InvalidOperationException>(8, "Other.");
        Assert.IsTrue(hostBuilder.Build().ExceptionMappings.TryResolve(new InvalidOperationException(), out _));

        CliHostOptions invalid = new(exceptionMappings: new ExceptionMappingRegistryBuilder()
            .Map<Exception>(1, static _ => null!).Build());
        Assert.ThrowsExactly<ArgumentNullException>(() => CliInvoker.Run<ExceptionHostCommand>([], invalid, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-008")]
    public async Task Builder_AndConstructorAgreeOnDiagnosticsCultureEnvironmentAndExceptionRendering()
    {
        using StringWriter builderOutput = new();
        using StringWriter builderErrors = new();
        using StringWriter constructorOutput = new();
        using StringWriter constructorErrors = new();
        CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        static string? Lookup(string name)
        {
            return name switch
            {
                "TEST_AMOUNT" => "1,5",
                "TEST_ITEMS" => "alpha;beta",
                _ => null
            };
        }
        ExceptionMappingRegistry mappings = new ExceptionMappingRegistryBuilder().Map<IOException>(9, "Safe.").Build();
        static string Render(CliExceptionRenderingContext context)
        {
            return "Rendered: " + context.Result.SafeMessage;
        }
        CliHostOptions builder = new CliHostOptionsBuilder()
            .WithStandardOutput(builderOutput).WithStandardError(builderErrors)
            .WithNumericCulture(culture).WithEnvironmentVariables(prefix: "TEST", lookup: Lookup, delimiter: ';')
            .AddExceptionMappings(mappings).WithExceptionRenderer(Render).Build();
        CliHostOptions constructor = new(standardOutput: constructorOutput, standardError: constructorErrors,
            numericCulture: culture, enableEnvironmentVariables: true, environmentVariablePrefix: "TEST",
            environmentVariableLookup: Lookup, environmentVariableDelimiter: ';',
            exceptionMappings: mappings, exceptionRenderer: Render);
        culture.NumberFormat.NumberDecimalSeparator = ".";

        OptionDefinition amount = new("amount", typeof(double), OptionArity.RequiredScalar);
        OptionDefinition items = new("items", typeof(IReadOnlyList<string>), OptionArity.GreedyCollection,
            elementType: typeof(string));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(amount).AddOption(items)
            .SetHandler((context, _) =>
            {
                Assert.IsTrue(context.TryGetOptionValue(amount, out double parsedAmount));
                Assert.AreEqual(1.5, parsedAmount);
                Assert.IsTrue(context.TryGetOptionValue(items, out IReadOnlyList<string>? parsedItems));
                Assert.AreSequenceEqual(["alpha", "beta"], [.. parsedItems!]);
                return 37;
            }));
        foreach (CliHostOptions options in new[] { builder, constructor })
        {
            Assert.AreEqual(37, await CliInvoker.RunAsync(model, [], options, TestContext.CancellationToken).ConfigureAwait(false));
            Assert.AreEqual(2, await CliInvoker.RunAsync(model, ["--unknown"], options, TestContext.CancellationToken).ConfigureAwait(false));
            Assert.AreEqual(9, await CliInvoker.RunAsync(CreateFaultModel(new IOException("secret")), [], options, TestContext.CancellationToken).ConfigureAwait(false));
        }

        string expectedErrors = "Argument 1: Unknown option." + Environment.NewLine + "Rendered: Safe." + Environment.NewLine;
        Assert.AreEqual(expectedErrors, builderErrors.ToString());
        Assert.AreEqual(expectedErrors, constructorErrors.ToString());
        await builderOutput.WriteAsync("still open").ConfigureAwait(false);
        await constructorOutput.WriteAsync("still open").ConfigureAwait(false);
        await builderErrors.WriteAsync("still open").ConfigureAwait(false);
        await constructorErrors.WriteAsync("still open").ConfigureAwait(false);
        Assert.AreEqual("still open", builderOutput.ToString());
        Assert.AreEqual("still open", constructorOutput.ToString());
        Assert.AreEqual(expectedErrors + "still open", builderErrors.ToString());
        Assert.AreEqual(expectedErrors + "still open", constructorErrors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-008")]
    [SuppressMessage("Usage", "CA1849:Call async methods when in an async method", Justification = "Verifies constructor/builder parity across both host execution styles.")]
    public async Task Builder_PreservesSnapshotsAndConstructorBehavior()
    {
        using StringWriter firstErrors = new();
        using StringWriter secondErrors = new();
        ExceptionMappingRegistry initial = new ExceptionMappingRegistryBuilder().Map<IOException>(9, "I/O.").Build();
        CliHostOptionsBuilder builder = new CliHostOptionsBuilder().WithStandardError(firstErrors).AddExceptionMappings(initial);
        CliHostOptions first = builder.Build();
        CliHostOptions second = builder.WithStandardError(secondErrors)
            .MapException<InvalidOperationException>(8, "Other.")
            .WithExceptionRenderer(static context => "Custom: " + context.Result.SafeMessage)
            .Build();
        CommandModel other = CreateFaultModel(new InvalidOperationException("secret"));
        Assert.ThrowsExactly<InvalidOperationException>(() => CliInvoker.Run(other, [], first, TestContext.CancellationToken));
        Assert.AreEqual(8, CliInvoker.Run(other, [], second, TestContext.CancellationToken));
        Assert.AreEqual(9, CliInvoker.Run(CreateFaultModel(new IOException("secret")), [], first, TestContext.CancellationToken));
        Assert.AreEqual("I/O." + Environment.NewLine, firstErrors.ToString());
        Assert.AreEqual("Custom: Other." + Environment.NewLine, secondErrors.ToString());

        CliHostOptions defaults = new CliHostOptionsBuilder().Build();
        CliHostOptions constructorDefaults = new();
        Assert.AreSame(constructorDefaults.StandardOutput, defaults.StandardOutput);
        Assert.AreEqual(constructorDefaults.CancelledExitCode, defaults.CancelledExitCode);
        using StringWriter output = new();
        CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        ConverterRegistry converters = new ConverterRegistryBuilder().Register(static (_, _) =>
            ValueConversion.FromValue(42)).Build();
        CliSyntaxOptions syntax = CliSyntaxOptions.Default;
        CliHostOptions configured = new CliHostOptionsBuilder()
            .WithStandardOutput(output).WithStandardError(firstErrors)
            .WithHelpWidth(60).WithHelpRenderer(static (_, width) => "Help " + width.ToString(CultureInfo.InvariantCulture))
            .WithHelpTreeRenderer(static _ => "Tree").WithExitCodes(7, 8, 9).WithExecutableName("cli")
            .WithConverters(converters).WithNumericCulture(culture).WithSyntax(syntax)
            .WithEnvironmentVariables(prefix: "TEST", lookup: static _ => "from-env", delimiter: ';', enabledByDefault: false)
            .Build();
        CliHostOptions constructor = new(output, firstErrors, 60,
            static (_, width) => "Help " + width.ToString(CultureInfo.InvariantCulture), 7, 8, 9, "cli",
            static _ => "Tree", converters, culture, syntax, true, "TEST", static _ => "from-env", ';', false);
        culture.NumberFormat.NumberDecimalSeparator = ".";
        Assert.AreEqual(",", configured.NumericCulture.NumberFormat.NumberDecimalSeparator);
        foreach (CliHostOptions options in new[] { configured, constructor })
        {
            CommandModel helpModel = CommandModel.Create(new CommandBuilder("help"), includeStandardHelp: true);
            Assert.AreEqual(0, CliInvoker.Run(helpModel, ["--help"], options, TestContext.CancellationToken));
            Assert.AreEqual(0, CliInvoker.Run(helpModel, ["--help-tree"], options, TestContext.CancellationToken));
            Assert.AreEqual(7, CliInvoker.Run(helpModel, ["--unknown"], options, TestContext.CancellationToken));
            Assert.AreEqual(8, CliInvoker.Run(helpModel, [], options, TestContext.CancellationToken));
            OptionDefinition value = new("value", typeof(int), OptionArity.RequiredScalar, environmentVariableBinding: EnvironmentVariableBinding.Enabled);
            CommandModel environment = CommandModel.Create(new CommandBuilder("env").AddOption(value)
                .SetHandler((context, _) => context.TryGetOptionValue(value, out int number) ? number : -1));
            Assert.AreEqual(42, await CliInvoker.RunAsync(environment, [], options, TestContext.CancellationToken).ConfigureAwait(false));
        }

        Assert.AreEqual("Help 60" + Environment.NewLine + "Tree" + Environment.NewLine
            + "Help 60" + Environment.NewLine + "Tree" + Environment.NewLine, output.ToString());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CliHostOptionsBuilder().WithHelpWidth(0).Build());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CliHostOptionsBuilder().WithEnvironmentVariables(delimiter: '"').Build());
        Assert.ThrowsExactly<ArgumentException>(() => new CliHostOptionsBuilder().WithExecutableName(" ").Build());
    }

    private static CommandModel CreateFaultModel(Exception failure) =>
        CommandModel.Create(new CommandBuilder("fault").SetHandler((_, _) => throw failure));

    private sealed class ThrowingWriter(IOException failure) : StringWriter
    {
        public override void WriteLine(string? value) => throw failure;
    }
}

[CliCommand("exception-host", IsRoot = true)]
public sealed partial class ExceptionHostCommand
{
    [CliOption]
    public string SafeMessage { get; init; } = "Safe failure.";

    [CliHandler]
    public int Run() => throw new CliExitCodeException(74, SafeMessage, new InvalidOperationException("secret"));
}

[CliCommand("async-exception-host", IsRoot = true)]
public sealed partial class AsyncExceptionHostCommand
{
    [CliOption]
    public string SafeMessage { get; init; } = "Safe failure.";

    [CliHandler]
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        throw new CliExitCodeException(74, SafeMessage, new InvalidOperationException("secret"));
    }
}
