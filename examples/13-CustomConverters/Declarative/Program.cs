using System;
using System.Globalization;
using Mauve.Cli.Conversion;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace CustomConverters.Declarative;

[CliConverter<BuildCodeConverter>(CompletionSafe = true)]
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

[CliCommand("converters", IsRoot = true, Description = "Inspect statically registered custom converters.")]
[CliDirective<AuditDirective>]
internal sealed partial class ConvertersCommand
{
    [CliOption(IsRequired = true, Description = "Build code in B-positiveInteger format.")]
    public partial BuildCode Code { get; }

    [CliOption(IsRequired = true, Description = "Nonnegative hexadecimal int with a lowercase 0x prefix.")]
    [CliConverter<HexIntConverter>(CompletionSafe = true)]
    public partial int Mask { get; }

    [CliOption(IsRequired = true, Description = "Ordinary decimal int; the mask override does not affect it.")]
    public partial int Count { get; }

    [CliArgument(Description = "Another build code using the type-level converter.")]
    public partial BuildCode Input { get; }

    [CliHandler]
    private int Run()
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"code={Code.Value}; mask={Mask}; count={Count}; input={Input.Value}"));
        return 0;
    }
}

[CliDirective(Description = "Print a converted build code and a definition-local hexadecimal mask.")]
internal sealed partial class AuditDirective
{
    [CliOption(IsRequired = true)]
    public partial BuildCode Code { get; }

    [CliOption(IsRequired = true)]
    [CliConverter<HexIntConverter>(CompletionSafe = true)]
    public partial int Mask { get; }

    [CliHandler]
    private int Run()
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"audit: code={Code.Value}; mask={Mask}"));
        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<ConvertersCommand>(args);
}
