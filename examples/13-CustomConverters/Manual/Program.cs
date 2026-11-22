using System;
using System.Globalization;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace CustomConverters.Manual;

internal readonly record struct BuildCode(int Value);

internal readonly struct BuildCodeConverter : ICliValueConverter<BuildCode>
{
    public static ValueConversion<BuildCode> Convert(string value, ConversionContext context)
    {
        return value.StartsWith("B-", StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number > 0
                ? ValueConversion.FromValue(new BuildCode(number))
                : ValueConversion.Failure<BuildCode>("build-code.invalid");
    }
}

internal readonly struct HexIntConverter : ICliValueConverter<int>
{
    public static ValueConversion<int> Convert(string value, ConversionContext context)
    {
        return value.StartsWith("0x", StringComparison.Ordinal)
            && int.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int number)
            && number >= 0
                ? ValueConversion.FromValue(number)
                : ValueConversion.Failure<int>("hex.invalid");
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        OptionDefinition code = new("--code", typeof(BuildCode), OptionArity.RequiredScalar, isRequired: true,
            description: "Build code in B-positiveInteger format.");
        OptionDefinition mask = new("--mask", typeof(int), OptionArity.RequiredScalar, isRequired: true,
            description: "Nonnegative hexadecimal int with a lowercase 0x prefix.",
            converter: ValueConverterRegistration.Create<int, HexIntConverter>(completionSafe: true));
        OptionDefinition count = new("--count", typeof(int), OptionArity.RequiredScalar, isRequired: true,
            description: "Ordinary decimal int; the mask override does not affect it.");
        PositionalDefinition input = new("input", typeof(BuildCode),
            description: "Another build code using the model-level converter.");
        CommandModel model = CommandModel.Create(new CommandBuilder("converters")
        {
            Description = "Inspect statically registered custom converters."
        }
            .RegisterConverter<BuildCode, BuildCodeConverter>(completionSafe: true)
            .AddOption(code)
            .AddOption(mask)
            .AddOption(count)
            .AddPositional(input)
            .AddDirective(CreateAudit())
            .SetHandler((context, _) =>
            {
                BuildCode parsedCode = Read<BuildCode>(context, code);
                int parsedMask = Read<int>(context, mask);
                int parsedCount = Read<int>(context, count);
                BuildCode parsedInput = context.TryGetPositionalValue(input, out BuildCode value)
                    ? value : throw new InvalidOperationException("The input argument is unavailable.");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"code={parsedCode.Value}; mask={parsedMask}; count={parsedCount}; input={parsedInput.Value}"));
                return 0;
            }),
            includeStandardHelp: true);
        return CliInvoker.Run(model, args);
    }

    private static DirectiveBuilder CreateAudit()
    {
        DirectiveValueDefinition code = new("code", typeof(BuildCode));
        DirectiveValueDefinition mask = new("mask", typeof(int),
            converter: ValueConverterRegistration.Create<int, HexIntConverter>(completionSafe: true));
        return new DirectiveBuilder("audit")
        {
            Description = "Print a converted build code and a definition-local hexadecimal mask."
        }
            .AddValue(code)
            .AddValue(mask)
            .SetHandler((context, _) =>
            {
                BuildCode parsedCode = context.TryGetValue(code, out BuildCode codeValue)
                    ? codeValue : throw new InvalidOperationException("The directive code is unavailable.");
                int parsedMask = context.TryGetValue(mask, out int maskValue)
                    ? maskValue : throw new InvalidOperationException("The directive mask is unavailable.");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"audit: code={parsedCode.Value}; mask={parsedMask}"));
                return 0;
            });
    }

    private static T Read<T>(CommandInvocationContext context, OptionDefinition option)
        => context.TryGetOptionValue(option, out T? value) && value is not null
            ? value : throw new InvalidOperationException("The required option is unavailable.");
}
