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
    public partial sbyte SignedByte { get; }

    [CliOption(IsRequired = true)]
    public partial byte UnsignedByte { get; }

    [CliOption(IsRequired = true)]
    public partial short SignedShort { get; }

    [CliOption(IsRequired = true)]
    public partial ushort UnsignedShort { get; }

    [CliOption(IsRequired = true)]
    public partial int SignedInt { get; }

    [CliOption(IsRequired = true)]
    public partial uint UnsignedInt { get; }

    [CliOption(IsRequired = true)]
    public partial long SignedLong { get; }

    [CliOption(IsRequired = true)]
    public partial ulong UnsignedLong { get; }

    [CliOption(IsRequired = true)]
    public partial Half Half { get; }

    [CliOption(IsRequired = true)]
    public partial float Single { get; }

    [CliOption(IsRequired = true)]
    public partial double Double { get; }

    [CliOption(IsRequired = true)]
    public partial decimal Decimal { get; }

    [CliOption]
    public partial bool Enabled { get; }

    [CliHandler]
    private int Run()
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
    public partial string Text { get; }

    [CliOption(IsRequired = true)]
    public partial char Letter { get; }

    [CliOption(IsRequired = true)]
    public partial Guid Id { get; }

    [CliOption(IsRequired = true)]
    public partial Uri Uri { get; }

    [CliOption(IsRequired = true)]
    public partial Version Version { get; }

    [CliOption(IsRequired = true)]
    public partial IPAddress Address { get; }

    [CliOption(IsRequired = true)]
    public partial FileInfo File { get; }

    [CliOption(IsRequired = true)]
    public partial DirectoryInfo Directory { get; }

    [CliOption(IsRequired = true)]
    public partial DisplayMode Mode { get; }

    [CliHandler]
    private int Run()
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
    public partial DateOnly Date { get; }

    [CliOption(IsRequired = true)]
    public partial TimeOnly Time { get; }

    [CliOption(IsRequired = true)]
    public partial DateTimeOffset Timestamp { get; }

    [CliOption(IsRequired = true)]
    public partial TimeSpan Duration { get; }

    [CliHandler]
    private int Run()
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
    public partial DateOnly? MaybeDate { get; }

    [CliOption("sample", Arity = OptionArity.RepeatedSingleCollection, IsRequired = true)]
    public partial IReadOnlyList<Half> Samples { get; }

    [CliOption(IsRequired = true)]
    public partial PortOrName Target { get; }

    [CliOption("route", Arity = OptionArity.RepeatedSingleCollection, IsRequired = true)]
    public partial IReadOnlyList<PortOrName> Routes { get; }

    [CliHandler]
    private int Run()
    {
        string date = TryGetMaybeDate(out DateOnly? value)
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
