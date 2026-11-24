using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Fuzzing;

internal enum FuzzMode
{
    Off,
    Read,
    Write
}

/// <summary>Fixed public-API command model shared by the parser, completion and environment targets.</summary>
internal sealed class RichModel
{
    internal static readonly CliLimits Limits = new(
        maximumArgumentCount: 16, maximumTokenLength: 128,
        maximumTotalCodeUnits: 1024, maximumDiagnostics: 8);

    internal static readonly RichModel Instance = new();

    private static readonly string[] Labels = ["alpha", "beta", "gamma", "al pha", "\"quoted\"", "back\\slash"];

    private RichModel()
    {
        Count = new OptionDefinition("count", typeof(int), OptionArity.RequiredScalar, shortName: 'c',
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: 5,
            validators: [CliValidators.Range(1, 100)]);
        Mode = new OptionDefinition("mode", typeof(FuzzMode), OptionArity.RequiredScalar, shortName: 'm');
        Ratio = new OptionDefinition("ratio", typeof(double), OptionArity.OptionalScalar,
            bareValuePolicy: BareValuePolicy.Constant, bareValue: 1.0);
        Flag = new OptionDefinition("flag", typeof(bool), OptionArity.BooleanFlag, shortName: 'f',
            aliases: [new OptionAlias("no-flag", OptionAliasKind.Negating)]);
        Verbose = new OptionDefinition("verbose", typeof(int), OptionArity.Counting, shortName: 'v',
            scope: OptionScope.Inherited);
        Quiet = new OptionDefinition("quiet", typeof(bool), OptionArity.BooleanFlag, shortName: 'q');
        Tags = new OptionDefinition("tag", typeof(IReadOnlyList<string>), OptionArity.RepeatedSingleCollection,
            elementType: typeof(string), validators: [CliValidators.CollectionLength<string>(0, 4)]);
        Files = new OptionDefinition("files", typeof(IReadOnlyList<int>), OptionArity.GreedyCollection,
            elementType: typeof(int), validators: [CliValidators.Each<int>(CliValidators.Minimum(0))]);
        Label = new OptionDefinition("label", typeof(string), OptionArity.RequiredScalar,
            validators: [CliValidators.AllowedValues("alpha", "beta", "gamma")]);
        Name = new OptionDefinition("name", typeof(string), OptionArity.RequiredScalar,
            validators: [CliValidators.StringLength(0, 100)]);
        Secret = new OptionDefinition("secret", typeof(string), OptionArity.RequiredScalar, isSensitive: true,
            validators: [new ValueValidator<string>(static (value, _) =>
                value is not null && value.StartsWith("bad", StringComparison.Ordinal)
                    ? ValueValidationOutcome.Failure("secret.bad", "Rejected.")
                    : ValueValidationOutcome.Success)]);
        Big = new OptionDefinition("big", typeof(long), OptionArity.RequiredScalar);
        Level = new OptionDefinition("level", typeof(byte), OptionArity.RequiredScalar);
        Amount = new OptionDefinition("amount", typeof(decimal), OptionArity.RequiredScalar);
        Target = new OptionDefinition("target", typeof(string), OptionArity.RequiredScalar, isRequired: true, shortName: 't');
        DryRun = new OptionDefinition("dry-run", typeof(bool), OptionArity.BooleanFlag);
        Depth = new OptionDefinition("depth", typeof(int), OptionArity.RequiredScalar,
            validators: [CliValidators.Maximum(10)]);
        Path = new PositionalDefinition("path", typeof(string));
        Rest = new PositionalDefinition("rest", typeof(IReadOnlyList<int>), minimumValues: 0, maximumValues: null,
            elementType: typeof(int));
        Filter = new PositionalDefinition("filter", typeof(string), minimumValues: 0, maximumValues: 1,
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: "all");

        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterEnum(
            [
                new KeyValuePair<string, FuzzMode>("off", FuzzMode.Off),
                new KeyValuePair<string, FuzzMode>("read", FuzzMode.Read),
                new KeyValuePair<string, FuzzMode>("write", FuzzMode.Write)
            ])
            .Build();

        Providers = new CompletionProviderRegistryBuilder()
            .Register(Label, ProvideLabels)
            .Register(Path, ProvidePaths, CompletionDependency.ForOption(Label))
            .Build();

        Model = CommandModel.Create(new CommandBuilder("tool")
            .AddConverters(converters)
            .AddOption(Count).AddOption(Mode).AddOption(Ratio).AddOption(Flag).AddOption(Verbose)
            .AddOption(Quiet).AddOption(Tags).AddOption(Files).AddOption(Label).AddOption(Name)
            .AddOption(Secret).AddOption(Big).AddOption(Level).AddOption(Amount)
            .AddDirective(new DirectiveBuilder("trace").SetHandler((_, _) =>
            {
                Directives++;
                return DirectiveResult.ContinueProcessing;
            }))
            .AddDirective(new DirectiveBuilder("set")
                .AddValue(new DirectiveValueDefinition("value", typeof(string)))
                .SetHandler((_, _) =>
                {
                    Directives++;
                    return DirectiveResult.ContinueProcessing;
                }))
            .AddDirective(new DirectiveBuilder("level")
                .AddValue(new DirectiveValueDefinition("amount", typeof(int), isRequired: false))
                .SetHandler((_, _) =>
                {
                    Directives++;
                    return DirectiveResult.ContinueProcessing;
                }))
            .AddCrossValidator(context => context.WasProvided(Verbose) && context.WasProvided(Quiet)
                ? ValueValidationOutcome.Failure("cross.loud", Quiet, message: "Verbose and quiet conflict.")
                : ValueValidationOutcome.Success)
            .SetHandler((_, _) => { Commands++; return 0; })
            .AddChild(new CommandBuilder("run").AddAlias("r")
                .AddOption(Target).AddOption(DryRun).AddPositional(Path).AddPositional(Rest)
                .SetHandler((_, _) => { Commands++; return 0; })
                .AddChild(new CommandBuilder("deep").AddOption(Depth)
                    .SetHandler((_, _) => { Commands++; return 0; })))
            .AddChild(new CommandBuilder("list").AddPositional(Filter)
                .SetHandler((_, _) => { Commands++; return 0; }))
            .AddChild(new CommandBuilder("debug") { IsHidden = true }
                .SetHandler((_, _) => { Commands++; return 0; })),
            Limits, includeStandardHelp: true);
    }

    internal static int Commands { get; private set; }

    internal static int Directives { get; private set; }

    internal CommandModel Model { get; }

    internal CompletionProviderRegistry Providers { get; }

    internal OptionDefinition Count { get; }

    internal OptionDefinition Mode { get; }

    internal OptionDefinition Ratio { get; }

    internal OptionDefinition Flag { get; }

    internal OptionDefinition Verbose { get; }

    internal OptionDefinition Quiet { get; }

    internal OptionDefinition Tags { get; }

    internal OptionDefinition Files { get; }

    internal OptionDefinition Label { get; }

    internal OptionDefinition Name { get; }

    internal OptionDefinition Secret { get; }

    internal OptionDefinition Big { get; }

    internal OptionDefinition Level { get; }

    internal OptionDefinition Amount { get; }

    internal OptionDefinition Target { get; }

    internal OptionDefinition DryRun { get; }

    internal OptionDefinition Depth { get; }

    internal PositionalDefinition Path { get; }

    internal PositionalDefinition Rest { get; }

    internal PositionalDefinition Filter { get; }

    /// <summary>Environment variable names for every option eligible for environment binding.</summary>
    internal IReadOnlyList<string> EnvironmentNames { get; } =
    [
        "TOOL_COUNT", "TOOL_MODE", "TOOL_RATIO", "TOOL_FLAG", "TOOL_VERBOSE", "TOOL_QUIET", "TOOL_TAG",
        "TOOL_FILES", "TOOL_LABEL", "TOOL_NAME", "TOOL_SECRET", "TOOL_BIG", "TOOL_LEVEL", "TOOL_AMOUNT"
    ];

    internal static void Reset()
    {
        Commands = 0;
        Directives = 0;
    }

    private static async IAsyncEnumerable<CompletionValue> ProvideLabels(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (string label in Labels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (label.StartsWith(context.Prefix, StringComparison.Ordinal))
            {
                yield return new CompletionValue(label, description: "label");
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<CompletionValue> ProvidePaths(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string label = "none";
        foreach (CompletionDependency dependency in context.Dependencies)
        {
            if (dependency.Option is { } option && context.TryGetOptionValue(option, out string? value) && value is not null)
            {
                label = value;
            }
        }

        foreach (string suffix in (string[])["a", "b c", "-d"])
        {
            cancellationToken.ThrowIfCancellationRequested();
            string candidate = label + "/" + suffix;
            if (candidate.StartsWith(context.Prefix, StringComparison.Ordinal))
            {
                yield return new CompletionValue(candidate);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
