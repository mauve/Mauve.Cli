using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Aot.Tests;

#if NET11_0_OR_GREATER
internal readonly union AotChoice(int, BuildCode);
#endif

internal readonly record struct AotHexConverter : ICliValueConverter<int>
{
    public static ValueConversion<int> Convert(string value, ConversionContext context)
        => int.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int number)
            ? ValueConversion.FromValue(number) : ValueConversion.Failure<int>("hex.invalid");
}

[CliCommand(IsRoot = true)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated dispatcher instantiates this command at invocation.")]
internal sealed partial class AotConversionCommand
{
    [Required]
    [CliOption]
    public AotChoice Choice { get; init; }

    [CliOption]
    public string Flavor { get; init; } = "classic";

    [CliOption(IsRequired = true)]
    [CliConverter<AotHexConverter>]
    public int Hex { get; init; }

    [CliOption(IsRequired = true)]
    public DateOnly Date { get; init; }

    [CliOption(IsRequired = true)]
    public required FileInfo File { get; init; }

    [CliOption(IsRequired = true)]
    public Half Ratio { get; init; }

    [CliArgument]
    public required IReadOnlyList<BuildCode?> Codes { get; init; }

    [CliHandler]
    internal int Run() => Flavor == "classic"
        ? AotConversionScenario.Score(Choice, Hex, Date, File, Ratio, Codes)
        : throw new InvalidOperationException("The generated constant default was not bound.");
}

internal static class AotConversionScenario
{
    internal static int Score(AotChoice choice, int hex, DateOnly date, FileInfo file, Half ratio, IReadOnlyList<BuildCode?> codes)
    {
        return choice.Value is int number && date == new DateOnly(2024, 2, 29)
            && file.Name == "not-created.txt" && ratio == (Half)1.5 && codes.Count == 2
            ? number + hex + codes[0]!.Value.Value + codes[1]!.Value.Value
            : throw new InvalidOperationException("Native conversion values differ from their declared types.");
    }

    internal static async Task VerifyAsync()
    {
        OptionDefinition choice = new("--choice", typeof(AotChoice), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition hex = new("--hex", typeof(int), OptionArity.RequiredScalar, isRequired: true,
            converter: ValueConverterRegistration.Create<int, AotHexConverter>());
        OptionDefinition date = new("--date", typeof(DateOnly), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition file = new("--file", typeof(FileInfo), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition ratio = new("--ratio", typeof(Half), OptionArity.RequiredScalar, isRequired: true);
        PositionalDefinition codes = new("codes", typeof(IReadOnlyList<BuildCode?>), maximumValues: null, elementType: typeof(BuildCode?));
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register<BuildCode, BuildCodeConverter>().RegisterNullable<BuildCode>()
            .RegisterUnion<AotChoice>(union => union.Case<int>(static value => new(value)).Case<BuildCode>(static value => new(value)))
            .Build();
        CommandModel manual = CommandModel.Create(new CommandBuilder("conversion")
            .AddConverters(converters).AddOption(choice).AddOption(hex).AddOption(date).AddOption(file).AddOption(ratio).AddPositional(codes)
            .SetHandler((context, _) => context.TryGetOptionValue(choice, out AotChoice parsedChoice)
                && context.TryGetOptionValue(hex, out int parsedHex)
                && context.TryGetOptionValue(date, out DateOnly parsedDate)
                && context.TryGetOptionValue(file, out FileInfo? parsedFile)
                && context.TryGetOptionValue(ratio, out Half parsedRatio)
                && context.TryGetPositionalValue(codes, out IReadOnlyList<BuildCode?>? parsedCodes)
                    ? Score(parsedChoice, parsedHex, parsedDate, parsedFile!, parsedRatio, parsedCodes!)
                    : throw new InvalidOperationException("Explicit NativeAOT conversion binding failed.")));
        foreach (CommandModel model in new[] { manual, AotConversionCommand.CreateModel() })
        {
            InvocationResult result = await new CliInvoker().InvokeAsync(model,
                ["--choice=12", "--hex=FF", "--date=2024-02-29", "--file=not-created.txt", "--ratio=1.5", "23", "34"]).ConfigureAwait(false);
            if (result.Outcome != InvocationOutcome.Completed || result.ExitCode != 324)
            {
                throw new InvalidOperationException("Generated and explicit union/converter NativeAOT behavior differs.");
            }

            ParseResult invalid = new CliParser().Parse(model, ["--choice=bad", "--hex=bad-hex", "--date=not-a-date", "--file=", "--ratio=NaN", "bad"]);
            if (invalid.Outcome != ParseOutcome.Invalid || invalid.Values.Count != 0)
            {
                throw new InvalidOperationException("Invalid native conversion input exposed values.");
            }
        }
    }
}
