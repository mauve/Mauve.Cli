using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Parsing;

[TestClass]
public sealed class BuiltInConversionTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-001")]
    public void Parse_ConvertsNewScalarTypes()
    {
        AssertConverted("1.5e2", (Half)150);
        AssertConverted("-0.5", (Half)(-0.5));
        AssertConverted("x", 'x');
        AssertConverted("2024-02-29", new DateOnly(2024, 2, 29));
        AssertConverted("09:03", new TimeOnly(9, 3));
        AssertConverted("09:03:04", new TimeOnly(9, 3, 4));
        AssertConverted("09:03:04.1234567", new TimeOnly(9, 3, 4).Add(TimeSpan.FromTicks(1234567)));
        AssertConverted("2024-02-29T09:03:04Z", new DateTimeOffset(2024, 2, 29, 9, 3, 4, TimeSpan.Zero));
        AssertConverted("2024-02-29T09:03:04.1+05:30", new DateTimeOffset(2024, 2, 29, 9, 3, 4, TimeSpan.FromMinutes(330)).AddTicks(1000000));
        AssertConverted("2024-02-29T09:03-07:00", new DateTimeOffset(2024, 2, 29, 9, 3, 0, TimeSpan.FromHours(-7)));
        AssertConverted("-1.02:03:04.1234567", -new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234567)));
        AssertConverted("1.2", new Version(1, 2));
        AssertConverted("1.2.3.4", new Version(1, 2, 3, 4));
        AssertConverted("192.168.1.1", IPAddress.Parse("192.168.1.1"));
        AssertConverted("::ffff:192.168.1.1", IPAddress.Parse("::ffff:192.168.1.1"));
        AssertConverted("2001:db8::1", IPAddress.Parse("2001:db8::1"));

        CommandModel model = ScalarModel<DateTimeOffset>();
        ParseResult offset = new CliParser().Parse(model, ["--value=2024-02-29T09:03:04+05:30"]);
        Assert.IsTrue(offset.TryGetOptionValue(model.Root, model.Root.Options[0], out DateTimeOffset timestamp));
        Assert.AreEqual(TimeSpan.FromMinutes(330), timestamp.Offset);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-004")]
    public void Parse_RejectsMalformedNewTypesWithSafeDiagnostics()
    {
        AssertInvalid<Half>("NaN", "Infinity", "65536", " 1", "1,5");
        AssertInvalid<char>("", "ab", "\ud800", "\udc00", "\ud83d\ude00");
        AssertInvalid<DateOnly>("2023-02-29", "2024-2-29", "2024-02-29 ", "02/29/2024");
        AssertInvalid<TimeOnly>("9:03", "24:00", "09:03:04.", "09:03:04.12345678", "09:03.1");
        AssertInvalid<DateTimeOffset>("2024-02-29T09:03:04", "2024-02-29 09:03:04Z", "2024-02-29T09:03:04+14:01", "2024-02-29T09:03:04.12345678Z", "2024-02-29T09:03:04z");
        AssertInvalid<TimeSpan>(" 01:02:03", "1:60:00", "1 day", "1:02:03 ");
        AssertInvalid<Version>("1", "1.2.3.4.5", "+1.2", "1.-2", "1..2", "1.٢", "2147483648.1");
        AssertInvalid<IPAddress>("127.1", "127.0.0", "127.00.0.1", "0x7f000001", "2130706433", "256.1.1.1", "::ffff:192.168.01.1", "[::1]", " ::1");
        AssertInvalid<FileInfo>("", "\0");
        AssertInvalid<DirectoryInfo>("", "\0");
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-002")]
    public void Parse_HalfUsesSelectedNumericCultureOnly()
    {
        CommandModel model = ScalarModel<Half>();
        ParseResult comma = new CliParser(numericCulture: CultureInfo.GetCultureInfo("fr-FR")).Parse(model, ["--value=1,5"]);
        ParseResult invariant = new CliParser().Parse(model, ["--value=1,5"]);

        Assert.AreEqual(ParseOutcome.Validated, comma.Outcome);
        Assert.IsTrue(comma.TryGetOptionValue(model.Root, model.Root.Options[0], out Half result));
        Assert.AreEqual((Half)1.5, result);
        Assert.AreEqual(ParseOutcome.Invalid, invariant.Outcome);
        ParseResult negative = new CliParser().Parse(model, ["--value", "-0.5"]);
        Assert.AreEqual(ParseOutcome.Validated, negative.Outcome);
        Assert.IsTrue(negative.TryGetOptionValue(model.Root, model.Root.Options[0], out Half negativeValue));
        Assert.AreEqual((Half)(-0.5), negativeValue);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-001")]
    public void Parse_PathsAreLexicalAndNeedNotExist()
    {
        string path = Path.Combine(Path.GetTempPath(), "mauve-nonexistent-" + Guid.NewGuid().ToString("N"), "item");
        CommandModel fileModel = ScalarModel<FileInfo>();
        CommandModel directoryModel = ScalarModel<DirectoryInfo>();
        ParseResult file = new CliParser().Parse(fileModel, ["--value=" + path]);
        ParseResult directory = new CliParser().Parse(directoryModel, ["--value=" + path]);

        Assert.AreEqual(ParseOutcome.Validated, file.Outcome);
        Assert.AreEqual(ParseOutcome.Validated, directory.Outcome);
        Assert.IsTrue(file.TryGetOptionValue(fileModel.Root, fileModel.Root.Options[0], out FileInfo? fileInfo));
        Assert.IsTrue(directory.TryGetOptionValue(directoryModel.Root, directoryModel.Root.Options[0], out DirectoryInfo? directoryInfo));
        Assert.AreEqual(path, fileInfo!.FullName);
        Assert.AreEqual(path, directoryInfo!.FullName);
        Assert.IsFalse(File.Exists(path));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-001")]
    public void Parse_CreatesTypedCollectionsAndNullableValues()
    {
        AssertCollection("1.5", (Half)1.5);
        AssertCollection<Half?>("1.5", (Half)1.5);
        AssertCollection("x", 'x');
        AssertCollection<char?>("x", 'x');
        AssertCollection("2024-02-29", new DateOnly(2024, 2, 29));
        AssertCollection<DateOnly?>("2024-02-29", new DateOnly(2024, 2, 29));
        AssertCollection("09:03", new TimeOnly(9, 3));
        AssertCollection<TimeOnly?>("09:03", new TimeOnly(9, 3));
        AssertCollection("2024-02-29T09:03Z", new DateTimeOffset(2024, 2, 29, 9, 3, 0, TimeSpan.Zero));
        AssertCollection<DateTimeOffset?>("2024-02-29T09:03Z", new DateTimeOffset(2024, 2, 29, 9, 3, 0, TimeSpan.Zero));
        AssertCollection("01:02:03", new TimeSpan(1, 2, 3));
        AssertCollection<TimeSpan?>("01:02:03", new TimeSpan(1, 2, 3));
        AssertCollection("1.2", new Version(1, 2));
        AssertCollection("::1", IPAddress.IPv6Loopback);
        AssertCollection("relative-file", new FileInfo("relative-file"), static (left, right) => left.FullName == right.FullName);
        AssertCollection("relative-directory", new DirectoryInfo("relative-directory"), static (left, right) => left.FullName == right.FullName);
        AssertConverted<DateOnly?>("2024-02-29", new DateOnly(2024, 2, 29));
        AssertInvalid<DateOnly?>("");

        CommandModel nullable = ScalarModel<DateOnly?>();
        ParseResult absent = new CliParser().Parse(nullable, []);
        Assert.AreEqual(ParseOutcome.Validated, absent.Outcome);
        Assert.IsFalse(absent.TryGetOptionValue(nullable.Root, nullable.Root.Options[0], out DateOnly? _));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-004")]
    public void Parse_InvalidCollectionElementDoesNotCommitPartialValues()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--value", typeof(IReadOnlyList<IPAddress>), OptionArity.RepeatedSingleCollection, elementType: typeof(IPAddress))));
        ParseResult result = new CliParser().Parse(model, ["--value=::1", "--value=127.1"]);

        Assert.AreEqual(ParseOutcome.Invalid, result.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, result.Diagnostics[0].Code);
        Assert.AreEqual("conversion.invalid", result.Diagnostics[0].FailureCode);
        Assert.AreEqual(1, result.Diagnostics[0].ValueTokenIndex);
        Assert.IsEmpty(result.Values);
    }

    private static CommandModel ScalarModel<T>() => CommandModel.Create(new CommandBuilder("tool")
        .AddOption(new OptionDefinition("--value", typeof(T), OptionArity.RequiredScalar)));

    private static void AssertConverted<T>(string raw, T expected)
    {
        CommandModel model = ScalarModel<T>();
        ParseResult result = new CliParser().Parse(model, ["--value=" + raw]);
        Assert.AreEqual(ParseOutcome.Validated, result.Outcome, $"{typeof(T)}: {raw}");
        Assert.IsTrue(result.TryGetOptionValue(model.Root, model.Root.Options[0], out T? value));
        Assert.AreEqual(expected, value);
    }

    private static void AssertInvalid<T>(params string[] inputs)
    {
        CommandModel model = ScalarModel<T>();
        foreach (string input in inputs)
        {
            ParseResult result = new CliParser().Parse(model, ["--value=" + input]);
            Assert.AreEqual(ParseOutcome.Invalid, result.Outcome, $"{typeof(T)}: {input}");
            Assert.AreEqual(ParseDiagnosticCode.InvalidValue, result.Diagnostics[0].Code);
            Assert.AreEqual("conversion.invalid", result.Diagnostics[0].FailureCode);
            Assert.AreEqual(0, result.Diagnostics[0].ValueTokenIndex);
            Assert.AreEqual(8, result.Diagnostics[0].ValueOffset);
            Assert.IsFalse(result.Diagnostics[0].Message.Contains(input, StringComparison.Ordinal) && input.Length > 0);
            Assert.IsEmpty(result.Values);
        }
    }

    private static void AssertCollection<T>(string raw, T expected, Func<T, T, bool>? equals = null)
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--value", typeof(IReadOnlyList<T>), OptionArity.RepeatedSingleCollection, elementType: typeof(T))));
        ParseResult result = new CliParser().Parse(model, ["--value=" + raw, "--value=" + raw]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome, typeof(T).ToString());
        Assert.IsTrue(result.TryGetOptionValue(model.Root, model.Root.Options[0], out IReadOnlyList<T>? values));
        Assert.HasCount(2, values!);
        foreach (T value in values!)
        {
            Assert.IsTrue(equals is null ? EqualityComparer<T>.Default.Equals(expected, value) : equals(expected, value));
        }
    }
}
