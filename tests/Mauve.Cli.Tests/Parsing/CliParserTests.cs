using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Parsing;

[TestClass]
public sealed class CliParserTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-03")]
    [TestProperty(Metadata.Requirement, "FEAT-03-001")]
    public void Parse_PreservesArgumentBoundariesAndEmptyRestValues()
    {
        CommandModel model = CreateLeafModel();
        string[] arguments = ["--", "-file", "a b", string.Empty];

        ParseResult result = new CliParser().Parse(model, arguments);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetPositionalValue(
            model.Root,
            model.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        CollectionAssert.AreEqual(new[] { "-file", "a b", string.Empty }, paths!.ToArray());
        Assert.AreEqual(arguments.Length, result.Arguments.Count);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-03")]
    [TestProperty(Metadata.Requirement, "FEAT-03-002")]
    public void Parse_ReportsMissingValuesWithoutConsumingTheEndMarker()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar))
            .AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string))));

        ParseResult result = new CliParser().Parse(model, ["--count", "--", "--file"]);

        Assert.AreEqual(ParseOutcome.Invalid, result.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.MissingValue, result.Diagnostics[0].Code);
        Assert.AreEqual(0, result.Diagnostics[0].TokenIndex);
        Assert.AreEqual(0, result.Values.Count);
        Assert.AreEqual(0, result.UnmatchedTokens.Count);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-03")]
    [TestProperty(Metadata.Requirement, "FEAT-03-004")]
    public void Parse_TreatsDashTokensAsOptionsUntilTheEndMarker()
    {
        CommandModel model = CreateLeafModel();

        ParseResult result = new CliParser().Parse(model, ["--", "-file"]);
        ParseResult unknown = new CliParser().Parse(model, ["--wat"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetPositionalValue(
            model.Root,
            model.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        CollectionAssert.AreEqual(new[] { "-file" }, paths!.ToArray());
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, unknown.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-001")]
    public void Parse_ConvertsBuiltInsAndExplicitEnumMaps()
    {
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterEnum(
            [
                new KeyValuePair<string, AccessMode>("Read", AccessMode.Read),
                new KeyValuePair<string, AccessMode>("Write", AccessMode.Write)
            ])
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--mode", typeof(AccessMode), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser(converters).Parse(model, ["--count=42", "--mode=Read"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetOptionValue(model.Root, model.Root.Options[0], out int count));
        Assert.AreEqual(42, count);
        Assert.IsTrue(result.TryGetOptionValue(model.Root, model.Root.Options[1], out AccessMode mode));
        Assert.AreEqual(AccessMode.Read, mode);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-002")]
    public void Parse_UsesInvariantNumbersByDefaultAndSelectedDecimalSeparator()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--ratio", typeof(double), OptionArity.RequiredScalar)));
        CliParser invariantParser = new();
        CliParser commaParser = new(numericCulture: CultureInfo.GetCultureInfo("fr-FR"));

        ParseResult invariant = invariantParser.Parse(model, ["--ratio=1.5"]);
        ParseResult comma = commaParser.Parse(model, ["--ratio=1,5"]);
        ParseResult ambientMismatch = invariantParser.Parse(model, ["--ratio=1,5"]);

        Assert.AreEqual(ParseOutcome.Validated, invariant.Outcome);
        Assert.AreEqual(ParseOutcome.Validated, comma.Outcome);
        Assert.AreEqual(ParseOutcome.Invalid, ambientMismatch.Outcome);
        Assert.IsTrue(invariant.TryGetOptionValue(model.Root, model.Root.Options[0], out double invariantValue));
        Assert.IsTrue(comma.TryGetOptionValue(model.Root, model.Root.Options[0], out double commaValue));
        Assert.AreEqual(1.5d, invariantValue);
        Assert.AreEqual(1.5d, commaValue);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Parse_UsesRegisteredConvertersAndRejectsDuplicateRegistrations()
    {
        ConverterRegistryBuilder builder = new ConverterRegistryBuilder()
            .Register((value, _) => value == "read"
                ? ValueConversion.FromValue(AccessMode.Read)
                : ValueConversion.Failure<AccessMode>("mode.unknown"));
        ConverterRegistry converters = builder.Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--mode", typeof(AccessMode), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser(converters).Parse(model, ["--mode=read"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.ThrowsExactly<InvalidOperationException>(() => builder.Register(
            static (_, _) => ValueConversion.FromValue(AccessMode.Write)));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Parse_RunsConvertersInInputTokenOrder()
    {
        List<string> callOrder = [];
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register((value, _) =>
            {
                callOrder.Add(value);
                return value == "Read"
                    ? ValueConversion.FromValue(AccessMode.Read)
                    : ValueConversion.FromValue(AccessMode.Write);
            })
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--first", typeof(AccessMode), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--second", typeof(AccessMode), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser(converters).Parse(model, ["--second=Write", "--first=Read"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        CollectionAssert.AreEqual(new[] { "Write", "Read" }, callOrder);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-004")]
    public void Parse_ReportsSafeConversionFailuresWithoutPartialValues()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser().Parse(model, ["--count=2147483648"]);

        Assert.AreEqual(ParseOutcome.Invalid, result.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, result.Diagnostics[0].Code);
        Assert.AreEqual(0, result.Diagnostics[0].TokenIndex);
        Assert.AreEqual(0, result.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(8, result.Diagnostics[0].ValueOffset);
        Assert.IsFalse(result.Diagnostics[0].Message.Contains("2147483648", StringComparison.Ordinal));
        Assert.AreEqual(0, result.Values.Count);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Parse_PropagatesConverterFaults()
    {
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register<AccessMode>(static (_, _) => throw new InvalidOperationException("converter fault"))
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--mode", typeof(AccessMode), OptionArity.RequiredScalar)));

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new CliParser(converters).Parse(model, ["--mode=read"]));

        Assert.AreEqual("converter fault", exception.Message);
    }

    private static CommandModel CreateLeafModel()
    {
        return CommandModel.Create(new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string))));
    }

    private enum AccessMode
    {
        Read,
        Write
    }
}
