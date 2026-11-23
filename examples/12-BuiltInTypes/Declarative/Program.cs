using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace BuiltInTypes.Declarative;

[CliCommand("built-ins", IsRoot = true, Description = "Inspect built-in conversions without I/O.")]
[CliChild<NumbersCommand>]
[CliChild<IdentityCommand>]
[CliChild<TimeCommand>]
[CliChild<ShapesCommand>]
internal sealed partial class BuiltInsCommand
{
}

[CliCommand("numbers", Description = "Parse invariant numeric values and a boolean.")]
internal sealed partial class NumbersCommand
{
    [CliOption(IsRequired = true)]
    public sbyte SignedByte { get; init; }

    [CliOption(IsRequired = true)]
    public byte UnsignedByte { get; init; }

    [CliOption(IsRequired = true)]
    public short SignedShort { get; init; }

    [CliOption(IsRequired = true)]
    public ushort UnsignedShort { get; init; }

    [CliOption(IsRequired = true)]
    public int SignedInt { get; init; }

    [CliOption(IsRequired = true)]
    public uint UnsignedInt { get; init; }

    [CliOption(IsRequired = true)]
    public long SignedLong { get; init; }

    [CliOption(IsRequired = true)]
    public ulong UnsignedLong { get; init; }

    [CliOption(IsRequired = true)]
    public Half Half { get; init; }

    [CliOption(IsRequired = true)]
    public float Single { get; init; }

    [CliOption(IsRequired = true)]
    public double Double { get; init; }

    [CliOption(IsRequired = true)]
    public decimal Decimal { get; init; }

    [CliOption]
    public bool Enabled { get; init; }

    [CliHandler]
    internal int Run()
    {
        Output.Print("signed-byte", SignedByte);
        Output.Print("unsigned-byte", UnsignedByte);
        Output.Print("signed-short", SignedShort);
        Output.Print("unsigned-short", UnsignedShort);
        Output.Print("signed-int", SignedInt);
        Output.Print("unsigned-int", UnsignedInt);
        Output.Print("signed-long", SignedLong);
        Output.Print("unsigned-long", UnsignedLong);
        Output.Print("half", Half);
        Output.Print("single", Single);
        Output.Print("double", Double);
        Output.Print("decimal", Decimal);
        Output.Print("enabled", Enabled);
        return 0;
    }
}

[CliCommand("identity", Description = "Parse text, identifiers, addresses, lexical paths and an enum.")]
internal sealed partial class IdentityCommand
{
    [CliOption(IsRequired = true)]
    public required string Text { get; init; }

    [CliOption(IsRequired = true)]
    public char Letter { get; init; }

    [CliOption(IsRequired = true)]
    public Guid Id { get; init; }

    [CliOption(IsRequired = true)]
    public required Uri Uri { get; init; }

    [CliOption(IsRequired = true)]
    public required Version Version { get; init; }

    [CliOption(IsRequired = true)]
    public required IPAddress Address { get; init; }

    [CliOption(IsRequired = true)]
    public required FileInfo File { get; init; }

    [CliOption(IsRequired = true)]
    public required DirectoryInfo Directory { get; init; }

    [CliOption(IsRequired = true)]
    public DisplayMode Mode { get; init; }

    [CliHandler]
    internal int Run()
    {
        Output.Print("text", Text);
        Output.Print("letter", Letter);
        Output.Print("id", Id);
        Output.Print("uri", Uri);
        Output.Print("version", Version);
        Output.Print("address", Address);
        Output.Print("file", File.Name);
        Output.Print("directory", Directory.Name);
        Output.Print("mode", Mode);
        return 0;
    }
}

[CliCommand("time", Description = "Parse exact date, time, offset and duration formats.")]
internal sealed partial class TimeCommand
{
    [CliOption(IsRequired = true)]
    public DateOnly Date { get; init; }

    [CliOption(IsRequired = true)]
    public TimeOnly Time { get; init; }

    [CliOption(IsRequired = true)]
    public DateTimeOffset Timestamp { get; init; }

    [CliOption(IsRequired = true)]
    public TimeSpan Duration { get; init; }

    [CliHandler]
    internal int Run()
    {
        Output.Print("date", Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Output.Print("time", Time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        Output.Print("timestamp", Timestamp.ToString("O", CultureInfo.InvariantCulture));
        Output.Print("duration", Duration.ToString("c", CultureInfo.InvariantCulture));
        return 0;
    }
}

[CliCommand("shapes", Description = "Parse nullable, repeated and ordered union values.")]
internal sealed partial class ShapesCommand
{
    [CliOption(
        Arity = OptionArity.OptionalScalar,
        AllowsNull = true,
        BareValuePolicy = BareValuePolicy.Null)]
    public DateOnly? MaybeDate { get; init; }

    [CliOption("sample", Arity = OptionArity.RepeatedSingleCollection, IsRequired = true)]
    public required IReadOnlyList<Half> Samples { get; init; }

    [CliOption(IsRequired = true)]
    public PortOrName Target { get; init; }

    [CliOption("route", Arity = OptionArity.RepeatedSingleCollection, IsRequired = true)]
    public required IReadOnlyList<PortOrName> Routes { get; init; }

    [CliHandler]
    internal int Run(CommandInvocationContext context)
    {
        string date = context.TryGetOptionValue(context.Command.Options[0], out DateOnly? value)
            ? value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(null)"
            : "(absent)";
        Output.Print("maybe-date", date);
        foreach (Half sample in Samples)
        {
            Output.Print("sample", sample);
        }

        Output.Print("target", Output.Describe(Target));
        foreach (PortOrName route in Routes)
        {
            Output.Print("route", Output.Describe(route));
        }

        return 0;
    }
}

internal enum DisplayMode
{
    Compact,
    Detailed
}

#if NET11_0_OR_GREATER
internal readonly union PortOrName(int, string);
#endif

internal static class Output
{
    internal static void Print<T>(string name, T value)
        => Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}={value}"));

    internal static string Describe(PortOrName value) => value.Value switch
    {
        int port => string.Create(CultureInfo.InvariantCulture, $"port:{port}"),
        string name => $"name:{name}",
        _ => throw new InvalidOperationException("The union case is unavailable.")
    };
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<BuiltInsCommand>(args);
}
