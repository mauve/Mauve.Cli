using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;
using Mauve.Cli.Validation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Parsing;

[TestClass]
public sealed class CliParserTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-005")]
    [TestProperty(Metadata.Requirement, "FEAT-06-006")]
    public void Parse_BuiltInRulesAndCustomItemRejectionsUseValueCoordinates()
    {
        OptionDefinition count = new("count", typeof(int), OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: 5,
            validators: [CliValidators.Range(1, 10)]);
        OptionDefinition tags = new("tag", typeof(IReadOnlyList<string>), OptionArity.RepeatedSingleCollection,
            elementType: typeof(string),
            validators:
            [
                CliValidators.Each<string>(CliValidators.StringLength(2, 8)),
                CliValidators.Each<string>(new ValueValidator<string>(static (value, _) => value == "bad"
                    ? throw new CliValidationException(ValueValidationOutcome.FailureAt("tag.bad", 1, "This tag is reserved."))
                    : ValueValidationOutcome.Success))
            ]);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(count).AddOption(tags));
        CliParser parser = new();

        ParseResult scalar = parser.Parse(model, ["--count", "11"]);
        ParseResult item = parser.Parse(model, ["--tag=ok", "--tag", "bad"]);
        ParseResult inline = parser.Parse(model, ["--tag=ok", "--tag=x"]);
        ParseResult valid = parser.Parse(model, ["--count=10", "--tag=ok"]);

        Assert.AreEqual(ParseOutcome.Validated, valid.Outcome);
        Assert.AreEqual("range.invalid", scalar.Diagnostics[0].FailureCode);
        Assert.AreEqual(0, scalar.Diagnostics[0].TokenIndex);
        Assert.AreEqual(1, scalar.Diagnostics[0].ValueTokenIndex);
        StringAssert.Contains(scalar.Diagnostics[0].ToString(), "Value must be between 1 and 10 (inclusive).", StringComparison.Ordinal);
        Assert.AreEqual("tag.bad", item.Diagnostics[0].FailureCode);
        Assert.AreEqual(1, item.Diagnostics[0].TokenIndex);
        Assert.AreEqual(2, item.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(1, item.Diagnostics[0].ValueOffset);
        StringAssert.Contains(item.Diagnostics[0].ToString(), "Item 2: This tag is reserved.", StringComparison.Ordinal);
        Assert.AreEqual("length.invalid", inline.Diagnostics[0].FailureCode);
        Assert.AreEqual(1, inline.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(6, inline.Diagnostics[0].ValueOffset);
        StringAssert.Contains(inline.Diagnostics[0].ToString(), "Item 2: String length must be between 2 and 8 (inclusive).", StringComparison.Ordinal);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-005")]
    [TestProperty(Metadata.Requirement, "FEAT-06-006")]
    public void Parse_ExplainsCollectionAndCrossFailuresAndBoundsPublicMessages()
    {
        OptionDefinition tags = new("tag", typeof(IReadOnlyList<string>), OptionArity.RepeatedSingleCollection,
            elementType: typeof(string), validators: [CliValidators.CollectionLength<string>(2, 4)]);
        ParseResult collection = new CliParser().Parse(CommandModel.Create(new CommandBuilder("tool").AddOption(tags)), ["--tag=a"]);
        StringAssert.Contains(collection.Diagnostics[0].ToString(), "Collection count must be between 2 and 4 (inclusive).", StringComparison.Ordinal);

        ValueValidationOutcome rejection = ValueValidationOutcome.Failure("cross.bad", "Use a different combination.\n\u001b\u202e" + new string('a', 600));
        CommandModel cross = CommandModel.Create(new CommandBuilder("tool").AddCrossValidator(_ => rejection));
        ParseResult result = new CliParser().Parse(cross, []);
        StringAssert.Contains(result.Diagnostics[0].ToString(), "Use a different combination.", StringComparison.Ordinal);
        Assert.IsLessThanOrEqualTo(512, rejection.Message!.Length);
        Assert.IsFalse(result.Diagnostics[0].Message.Contains('\n', StringComparison.Ordinal));
        Assert.IsFalse(result.Diagnostics[0].Message.Contains('\u001b', StringComparison.Ordinal));
        Assert.IsFalse(result.Diagnostics[0].Message.Contains('\u202e', StringComparison.Ordinal));

        foreach (bool hidden in new[] { false, true })
        {
            OptionDefinition secret = new("secret", typeof(string), OptionArity.RequiredScalar,
                isSensitive: !hidden, isHidden: hidden,
                validators: [new ValueValidator<string>(static (_, _) => ValueValidationOutcome.Failure("secret.bad", "Private reason."))]);
            PositionalDefinition path = new("path", typeof(string), isSensitive: !hidden, isHidden: hidden,
                validators: [new ValueValidator<string>(static (_, _) => ValueValidationOutcome.Failure("secret.bad", "Private reason."))]);
            CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(secret).AddPositional(path));
            ParseResult redacted = new CliParser().Parse(model, ["--secret=value", "path"]);
            Assert.HasCount(2, redacted.Diagnostics);
            Assert.IsTrue(redacted.Diagnostics.All(static diagnostic => !diagnostic.ToString().Contains("Private reason.", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-006")]
    public void Parse_CrossValidationTargetsAndContractFaultsAreDistinguished()
    {
        OptionDefinition count = new("count", typeof(int), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(count)
            .AddCrossValidator(_ => throw new CliValidationException(ValueValidationOutcome.Failure("cross.bad", count, message: "Choose a smaller count."))));
        ParseResult result = new CliParser().Parse(model, ["--count=3"]);
        Assert.AreEqual("cross.bad", result.Diagnostics[0].FailureCode);
        Assert.AreEqual(0, result.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(8, result.Diagnostics[0].ValueOffset);
        StringAssert.Contains(result.Diagnostics[0].ToString(), "Choose a smaller count.", StringComparison.Ordinal);

        OptionDefinition foreign = new("other", typeof(int), OptionArity.RequiredScalar);
        CommandModel invalid = CommandModel.Create(new CommandBuilder("tool").AddOption(count)
            .AddCrossValidator(_ => ValueValidationOutcome.Failure("cross.bad", foreign)));
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => new CliParser().Parse(invalid, ["--count=3"]));

        OptionDefinition text = new("text", typeof(string), OptionArity.RequiredScalar,
            validators: [new ValueValidator<string>(static (_, _) => ValueValidationOutcome.FailureAt("bad.offset", 5))]);
        CommandModel invalidOffset = CommandModel.Create(new CommandBuilder("tool").AddOption(text));
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => new CliParser().Parse(invalidOffset, ["--text=abc"]));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-006")]
    public void Parse_HelpTreeHonorsHelpPrecedenceAndConflicts()
    {
        int converterCalls = 0;
        ConverterRegistry converters = new ConverterRegistryBuilder().Register((_, _) =>
        {
            converterCalls++;
            return ValueConversion.Failure<int>("integer.invalid");
        }).Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy")
                .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar, isRequired: true))),
            includeStandardHelp: true);
        CliParser parser = new(converters);
        ParseResult root = parser.Parse(model, ["--help-tree"]);
        ParseResult leaf = parser.Parse(model, ["copy", "--count=bad", "--help-tree"]);
        ParseResult invalid = parser.Parse(model, ["copy", "--wat", "--help-tree"]);
        ParseResult malformed = parser.Parse(model, ["copy", "--help-tree=maybe"]);
        ParseResult falseValue = parser.Parse(model, ["copy", "--help-tree=false"]);
        ParseResult repeated = parser.Parse(model, ["copy", "--help-tree", "--help-tree"]);
        ParseResult conflict = parser.Parse(model, ["copy", "--help", "--help-tree"]);
        ParseResult falseHelp = parser.Parse(model, ["copy", "--help=false", "--help-tree"]);

        Assert.AreEqual(ParseOutcome.HelpTreeRequested, root.Outcome);
        Assert.AreEqual(ParseOutcome.HelpTreeRequested, leaf.Outcome);
        Assert.AreEqual("copy", leaf.SelectedCommand!.Name);
        Assert.IsEmpty(leaf.Values);
        Assert.AreEqual(0, converterCalls);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, invalid.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, malformed.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, falseValue.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.DuplicateOption, repeated.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.ConflictingOptions, conflict.Diagnostics[0].Code);
        Assert.AreEqual(ParseOutcome.HelpTreeRequested, falseHelp.Outcome);

        CommandModel positional = CommandModel.Create(new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("value", typeof(string))), includeStandardHelp: true);
        ParseResult afterEnd = parser.Parse(positional, ["--", "--help-tree"]);
        Assert.AreEqual(ParseOutcome.Validated, afterEnd.Outcome);
        Assert.IsTrue(afterEnd.TryGetPositionalValue(positional.Root, positional.Root.Positionals[0], out string? value));
        Assert.AreEqual("--help-tree", value);
    }

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
        Assert.AreSequenceEqual(["-file", "a b", string.Empty], [.. paths!]);
        Assert.HasCount(arguments.Length, result.Arguments);
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
        Assert.IsEmpty(result.Values);
        Assert.IsEmpty(result.UnmatchedTokens);
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
        Assert.AreSequenceEqual(["-file"], [.. paths!]);
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
        Assert.AreSequenceEqual(["Write", "Read"], callOrder);
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
        Assert.IsEmpty(result.Values);
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

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-001")]
    public void Parse_HandlesBooleanFormsAndExplicitNegation()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--verbose",
                typeof(bool),
                OptionArity.BooleanFlag,
                aliases: [new OptionAlias("--no-verbose", OptionAliasKind.Negating)])));

        ParseResult enabled = new CliParser().Parse(model, ["--verbose"]);
        ParseResult disabled = new CliParser().Parse(model, ["--no-verbose"]);
        ParseResult spaceFalse = new CliParser().Parse(model, ["--verbose", "false"]);
        ParseResult invalid = new CliParser().Parse(model, ["--verbose=maybe"]);
        ParseResult repeated = new CliParser().Parse(model, ["--verbose", "--no-verbose"]);
        ParseResult unexpectedValue = new CliParser().Parse(model, ["--no-verbose=true"]);

        Assert.IsTrue(enabled.TryGetOptionValue(model.Root, model.Root.Options[0], out bool trueValue));
        Assert.IsTrue(trueValue);
        Assert.IsTrue(disabled.TryGetOptionValue(model.Root, model.Root.Options[0], out bool falseValue));
        Assert.IsFalse(falseValue);
        Assert.IsTrue(spaceFalse.TryGetOptionValue(model.Root, model.Root.Options[0], out bool spaceFalseValue));
        Assert.IsFalse(spaceFalseValue);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalid.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.DuplicateOption, repeated.Diagnostics[0].Code);
        Assert.AreEqual("Argument 2: Option '--verbose' cannot be repeated.", repeated.Diagnostics[0].ToString());
        Assert.AreEqual(ParseDiagnosticCode.UnexpectedValue, unexpectedValue.Diagnostics[0].Code);
        Assert.AreEqual(
            "Argument 1: Option '--verbose' does not accept a value in this form.",
            unexpectedValue.Diagnostics[0].ToString());

        CommandBuilder collidingNames = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--verbose",
                typeof(bool),
                OptionArity.BooleanFlag,
                aliases: [new OptionAlias("--no-verbose", OptionAliasKind.Negating)]))
            .AddOption(new OptionDefinition("--no-verbose", typeof(bool), OptionArity.BooleanFlag));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(collidingNames));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-002")]
    public void Parse_LeavesFollowingTokensForPositionalBindingAfterBareOptional()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--filter",
                typeof(string),
                OptionArity.OptionalScalar,
                allowsNull: true,
                bareValuePolicy: BareValuePolicy.Null))
            .AddPositional(new PositionalDefinition("name", typeof(string))));

        ParseResult result = new CliParser().Parse(model, ["--filter", "child"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetOptionValue(model.Root, model.Root.Options[0], out string? filter));
        Assert.IsNull(filter);
        Assert.IsTrue(result.TryGetPositionalValue(model.Root, model.Root.Positionals[0], out string? name));
        Assert.AreEqual("child", name);
        Assert.AreEqual(ValuePresence.Bare, result.Values[0].Presence);
        Assert.AreEqual(ValueOrigin.Explicit, result.Values[0].Origin);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-003")]
    public void Parse_AccumulatesCollectionOccurrencesAndRejectsScalarDuplicates()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--tag",
                typeof(IReadOnlyList<string>),
                OptionArity.RepeatedSingleCollection,
                elementType: typeof(string)))
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));

        ParseResult collection = new CliParser().Parse(model, ["--tag=a", "--tag=b"]);
        ParseResult duplicate = new CliParser().Parse(model, ["--count=1", "--count=2"]);

        Assert.IsTrue(collection.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<string>? tags));
        Assert.AreSequenceEqual(["a", "b"], [.. tags!]);
        IList<string> readOnlyTags = (IList<string>)tags!;
        Assert.IsTrue(readOnlyTags.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => readOnlyTags[0] = "changed");
        Assert.AreEqual("a", tags![0]);
        Assert.AreEqual(ParseDiagnosticCode.DuplicateOption, duplicate.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-003")]
    public void Parse_ConsumesGreedyCollectionsUntilTheNextOptionBoundary()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--tag",
                typeof(IReadOnlyList<string>),
                OptionArity.GreedyCollection,
                elementType: typeof(string)))
            .AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string))));

        ParseResult values = new CliParser().Parse(model, ["--tag", "a", "b", "--", "-file"]);
        ParseResult missing = new CliParser().Parse(model, ["--tag", "--"]);

        Assert.IsTrue(values.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<string>? tags));
        Assert.AreSequenceEqual(["a", "b"], [.. tags!]);
        Assert.IsTrue(values.TryGetPositionalValue(
            model.Root,
            model.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        Assert.AreSequenceEqual(["-file"], [.. paths!]);
        Assert.AreEqual(ParseDiagnosticCode.MissingValue, missing.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-004")]
    public void Parse_BindsRestValuesAndRejectsUnknownShortClusters()
    {
        CommandModel model = CreateLeafModel();

        ParseResult rest = new CliParser().Parse(model, ["--", "a", "-b", "c"]);
        ParseResult cluster = new CliParser().Parse(model, ["-ab"]);

        Assert.IsTrue(rest.TryGetPositionalValue(
            model.Root,
            model.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        Assert.AreSequenceEqual(["a", "-b", "c"], [.. paths!]);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, cluster.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-004")]
    public void Parse_ExpandsZeroValueShortClustersAndCountsExplicitCountingOptions()
    {
        OptionDefinition verbose = new(
            "verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            shortName: 'v');
        OptionDefinition verbosity = new(
            "verbosity",
            typeof(int),
            OptionArity.Counting,
            shortName: 'a');
        OptionDefinition filter = new(
            "filter",
            typeof(string),
            OptionArity.OptionalScalar,
            allowsNull: true,
            bareValuePolicy: BareValuePolicy.Constant,
            bareValue: "bare",
            shortName: 'f');
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(verbose)
            .AddOption(verbosity)
            .AddOption(filter));
        CliParser parser = new();

        ParseResult cluster = parser.Parse(model, ["-vaf"]);
        ParseResult repeats = parser.Parse(model, ["--verbosity", "--verbosity", "-v"]);

        Assert.AreEqual(ParseOutcome.Validated, cluster.Outcome);
        Assert.IsTrue(cluster.TryGetOptionValue(model.Root, verbose, out bool enabled));
        Assert.IsTrue(enabled);
        Assert.IsTrue(cluster.TryGetOptionValue(model.Root, verbosity, out int clusterCount));
        Assert.AreEqual(1, clusterCount);
        Assert.IsTrue(cluster.TryGetOptionValue(model.Root, filter, out string? bareFilter));
        Assert.AreEqual("bare", bareFilter);
        Assert.IsTrue(repeats.TryGetOptionValue(model.Root, verbosity, out int repeatCount));
        Assert.AreEqual(2, repeatCount);
        Assert.AreEqual(ParseOutcome.Validated, new CliParser().Parse(model, []).Outcome);
        Assert.AreEqual(
            ParseDiagnosticCode.UnexpectedValue,
            parser.Parse(model, ["--verbosity=2"]).Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-004")]
    public void Parse_RejectsInvalidClustersWithoutApplyingTheirValidPrefix()
    {
        OptionDefinition verbose = new("verbose", typeof(bool), OptionArity.BooleanFlag, shortName: 'v');
        OptionDefinition count = new("count", typeof(int), OptionArity.Counting, shortName: 'a');
        OptionDefinition output = new("output", typeof(string), OptionArity.RequiredScalar, shortName: 'o');
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(verbose)
            .AddOption(count)
            .AddOption(output));
        CliParser parser = new();

        ParseResult valueTaking = parser.Parse(model, ["-vo"]);
        ParseResult unknownCharacter = parser.Parse(model, ["-vz"]);
        ParseResult wrongCase = parser.Parse(model, ["-VV"]);

        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, valueTaking.Diagnostics[0].Code);
        Assert.AreEqual("-vo", valueTaking.UnmatchedTokens[0].Value);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, unknownCharacter.Diagnostics[0].Code);
        Assert.AreEqual("-vz", unknownCharacter.UnmatchedTokens[0].Value);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, wrongCase.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-03")]
    [TestProperty(Metadata.Requirement, "FEAT-03-002")]
    public void Parse_UsesConfiguredPrefixesAndKeepsTheEndMarkerFixed()
    {
        OptionDefinition output = new("output", typeof(string), OptionArity.RequiredScalar, shortName: 'o');
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(output));
        CliParser parser = new(syntax: new CliSyntaxOptions("++", "+"));

        ParseResult configured = parser.Parse(model, ["++output", "file"]);
        ParseResult shortOption = parser.Parse(model, ["+o", "short-file"]);
        ParseResult afterMarker = parser.Parse(model, ["--", "++output"]);

        Assert.AreEqual(ParseOutcome.Validated, configured.Outcome);
        Assert.IsTrue(configured.TryGetOptionValue(model.Root, output, out string? configuredValue));
        Assert.AreEqual("file", configuredValue);
        Assert.IsTrue(shortOption.TryGetOptionValue(model.Root, output, out string? shortValue));
        Assert.AreEqual("short-file", shortValue);
        Assert.AreEqual(ParseDiagnosticCode.UnexpectedArgument, afterMarker.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-001")]
    public void Parse_DistinguishesExplicitAndDefaultValuesAndKeepsRequiredDefaultsInert()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                defaultValuePolicy: DefaultValuePolicy.Constant,
                defaultValue: 0)));
        int factoryCalls = 0;
        CommandModel required = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--required",
                typeof(int),
                OptionArity.RequiredScalar,
                isRequired: true,
                defaultValuePolicy: DefaultValuePolicy.Factory,
                defaultValueFactory: () =>
                {
                    factoryCalls++;
                    return 4;
                })));

        ParseResult defaulted = new CliParser().Parse(model, []);
        ParseResult explicitResult = new CliParser().Parse(model, ["--count=0"]);
        ParseResult missing = new CliParser().Parse(required, []);

        Assert.IsTrue(defaulted.TryGetOptionValue(model.Root, model.Root.Options[0], out int defaultValue));
        Assert.IsTrue(explicitResult.TryGetOptionValue(model.Root, model.Root.Options[0], out int explicitValue));
        Assert.AreEqual(0, defaultValue);
        Assert.AreEqual(0, explicitValue);
        Assert.AreEqual(ValueOrigin.Default, defaulted.Values[0].Origin);
        Assert.AreEqual(ValueOrigin.Explicit, explicitResult.Values[0].Origin);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, missing.Diagnostics[0].Code);
        Assert.AreEqual(0, factoryCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-002")]
    public void Parse_InvokesAbsentFactoriesOncePerParseAndNotAfterInvalidInput()
    {
        int factoryCalls = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--items",
                typeof(IReadOnlyList<string>),
                OptionArity.RepeatedSingleCollection,
                elementType: typeof(string),
                defaultValuePolicy: DefaultValuePolicy.Factory,
                defaultValueFactory: () =>
                {
                    factoryCalls++;
                    return new List<string>();
                }))
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));
        CliParser parser = new();

        ParseResult first = parser.Parse(model, []);
        ParseResult second = parser.Parse(model, []);
        ParseResult invalid = parser.Parse(model, ["--count=bad"]);

        Assert.AreEqual(2, factoryCalls);
        Assert.IsTrue(first.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<string>? firstItems));
        Assert.IsTrue(second.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<string>? secondItems));
        Assert.AreNotSame(firstItems, secondItems);
        Assert.AreEqual(ParseOutcome.Invalid, invalid.Outcome);
        Assert.AreEqual(2, factoryCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-003")]
    public void Parse_RunsValueValidatorsOnAvailableValuesAndSkipsFailedConversions()
    {
        int validationCalls = 0;
        ValueValidator<int> validator = new((value, _) =>
        {
            validationCalls++;
            return value <= 10
                ? ValueValidationOutcome.Success
                : ValueValidationOutcome.Failure("range.invalid");
        });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                defaultValuePolicy: DefaultValuePolicy.Constant,
                defaultValue: 20,
                validators: [validator])));
        CliParser parser = new();

        ParseResult rejectedDefault = parser.Parse(model, []);
        int callsAfterDefault = validationCalls;
        ParseResult invalidConversion = parser.Parse(model, ["--count=bad"]);
        int callsAfterInvalidConversion = validationCalls;
        ParseResult valid = parser.Parse(model, ["--count=10"]);

        Assert.AreEqual(ParseDiagnosticCode.ValidationFailed, rejectedDefault.Diagnostics[0].Code);
        Assert.AreEqual(1, callsAfterDefault);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalidConversion.Diagnostics[0].Code);
        Assert.AreEqual(1, callsAfterInvalidConversion);
        Assert.AreEqual(ParseOutcome.Validated, valid.Outcome);
        Assert.AreEqual(2, validationCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-004")]
    public void Parse_RunsCrossValidationOnlyAfterValueValidation()
    {
        int crossCalls = 0;
        ValueValidator<bool> leftValidator = new((value, _) => value
            ? ValueValidationOutcome.Failure("value.invalid")
            : ValueValidationOutcome.Success);
        OptionDefinition left = new("--left", typeof(bool), OptionArity.RequiredScalar, validators: [leftValidator]);
        OptionDefinition right = new("--right", typeof(bool), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(left)
            .AddOption(right)
            .AddCrossValidator(context =>
            {
                crossCalls++;
                return context.WasProvided(left) && context.WasProvided(right)
                    ? ValueValidationOutcome.Failure("exclusive.conflict")
                    : ValueValidationOutcome.Success;
            }));
        CliParser parser = new();

        ParseResult conflict = parser.Parse(model, ["--left=false", "--right=false"]);
        ParseResult invalidValue = parser.Parse(model, ["--left=maybe"]);
        ParseResult valueRejected = parser.Parse(model, ["--left=true"]);
        ParseResult single = parser.Parse(model, ["--left=false"]);

        Assert.AreEqual(ParseDiagnosticCode.ValidationFailed, conflict.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalidValue.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.ValidationFailed, valueRejected.Diagnostics[0].Code);
        Assert.AreEqual("Values for command 'tool' failed validation.", conflict.Diagnostics[0].ToString());
        Assert.AreEqual("Argument 1: The value for option '--left' failed validation.", valueRejected.Diagnostics[0].ToString());
        Assert.AreEqual(ParseOutcome.Validated, single.Outcome);
        Assert.AreEqual(2, crossCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-003")]
    public void Parse_PropagatesValidatorFaults()
    {
        ValueValidator<int> validator = new(static (_, _) => throw new InvalidOperationException("validator fault"));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                validators: [validator])));

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new CliParser().Parse(model, ["--count=1"]));

        Assert.AreEqual("validator fault", exception.Message);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-004")]
    public void Parse_PropagatesCrossValidatorFaults()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddCrossValidator(static _ => throw new InvalidOperationException("cross-validator fault")));

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new CliParser().Parse(model, []));

        Assert.AreEqual("cross-validator fault", exception.Message);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-001")]
    public void ParsedValue_TryGetValueDistinguishesUnavailableNullAndIncompatibleValues()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("count", typeof(int), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition(
                "filter",
                typeof(string),
                OptionArity.OptionalScalar,
                allowsNull: true,
                bareValuePolicy: BareValuePolicy.Null)));
        CliParser parser = new();

        ParseResult present = parser.Parse(model, ["--count=7", "--filter"]);
        ParseResult absent = parser.Parse(model, []);
        ParsedValue count = present.Values[0];
        ParsedValue filter = present.Values[1];

        Assert.IsTrue(count.TryGetValue(out int number));
        Assert.AreEqual(7, number);
        Assert.IsTrue(filter.TryGetValue(out string? nullValue));
        Assert.IsNull(nullValue);
        Assert.IsFalse(absent.Values[0].TryGetValue(out int missing));
        Assert.AreEqual(0, missing);
        Assert.ThrowsExactly<InvalidOperationException>(() => count.TryGetValue<string>(out _));
        Assert.ThrowsExactly<InvalidOperationException>(() => filter.TryGetValue<int>(out _));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-001")]
    public void Parse_PreservesProvenanceAndIsolatesResultsAcrossParses()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                defaultValuePolicy: DefaultValuePolicy.Constant,
                defaultValue: 0)));
        CliParser parser = new();
        string[] arguments = ["--count=0"];

        ParseResult explicitResult = parser.Parse(model, arguments);
        arguments[0] = "--count=8";
        ParseResult defaultResult = parser.Parse(model, []);

        Assert.AreEqual("--count=0", explicitResult.Arguments[0]);
        Assert.IsTrue(explicitResult.TryGetOptionValue(model.Root, model.Root.Options[0], out int explicitValue));
        Assert.IsTrue(defaultResult.TryGetOptionValue(model.Root, model.Root.Options[0], out int defaultValue));
        Assert.AreEqual(0, explicitValue);
        Assert.AreEqual(0, defaultValue);
        Assert.AreEqual(ValueOrigin.Explicit, explicitResult.Values[0].Origin);
        Assert.AreEqual(ValueOrigin.Default, defaultResult.Values[0].Origin);
        Assert.AreNotSame(explicitResult.Values, defaultResult.Values);

        ParseResult? firstConcurrent = null;
        ParseResult? secondConcurrent = null;
        Parallel.Invoke(
            () => firstConcurrent = parser.Parse(model, ["--count=1"]),
            () => secondConcurrent = parser.Parse(model, ["--count=2"]));
        Assert.IsTrue(firstConcurrent!.TryGetOptionValue(model.Root, model.Root.Options[0], out int firstValue));
        Assert.IsTrue(secondConcurrent!.TryGetOptionValue(model.Root, model.Root.Options[0], out int secondValue));
        Assert.AreEqual(1, firstValue);
        Assert.AreEqual(2, secondValue);

        CommandModel otherModel = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));
        Assert.ThrowsExactly<ArgumentException>(() => explicitResult.TryGetOptionValue<int>(
            otherModel.Root,
            otherModel.Root.Options[0],
            out _));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-002")]
    public void Parse_ReportsStableCategoriesAndCoordinates()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));
        CliParser parser = new();

        ParseResult invalidValue = parser.Parse(model, ["--count=bad"]);
        ParseResult missingValue = parser.Parse(model, ["--count"]);
        ParseResult repeatedInvalidValue = parser.Parse(model, ["--count=bad"]);

        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalidValue.Diagnostics[0].Code);
        Assert.AreEqual(0, invalidValue.Diagnostics[0].TokenIndex);
        Assert.AreEqual(0, invalidValue.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(8, invalidValue.Diagnostics[0].ValueOffset);
        Assert.IsFalse(invalidValue.Diagnostics[0].Message.Contains("bad", StringComparison.Ordinal));
        Assert.AreEqual(ParseDiagnosticCode.MissingValue, missingValue.Diagnostics[0].Code);
        Assert.AreEqual("Argument 1: The value for option '--count' is invalid.", invalidValue.Diagnostics[0].ToString());
        Assert.AreEqual("Argument 1: Option '--count' requires a value.", missingValue.Diagnostics[0].ToString());
        ParseResult separateValue = parser.Parse(model, ["--count", "secret"]);
        Assert.AreEqual("Argument 2: The value for option '--count' is invalid.", separateValue.Diagnostics[0].ToString());
        Assert.AreEqual(invalidValue.Diagnostics[0].Code, repeatedInvalidValue.Diagnostics[0].Code);
        Assert.AreEqual(invalidValue.Diagnostics[0].TokenIndex, repeatedInvalidValue.Diagnostics[0].TokenIndex);

        CommandModel missingAndInvalid = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--required",
                typeof(string),
                OptionArity.RequiredScalar,
                isRequired: true))
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));
        ParseResult ordered = parser.Parse(missingAndInvalid, ["--count=bad"]);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, ordered.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, ordered.Diagnostics[1].Code);
        Assert.AreEqual(0, ordered.Diagnostics[0].ValueTokenIndex);
        Assert.IsNull(ordered.Diagnostics[1].TokenIndex);
        Assert.AreEqual("Command 'tool': Required option '--required' is missing.", ordered.Diagnostics[1].ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-002")]
    public void Parse_NamesMissingVisibleArgumentsWithoutInventingPositions()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("copy")
            .AddPositional(new PositionalDefinition("source", typeof(string)))
            .AddPositional(new PositionalDefinition("destination", typeof(string)))
            .AddPositional(new PositionalDefinition("internal-name", typeof(string), isHidden: true)));
        ParseResult missing = new CliParser().Parse(model, ["source.txt"]);

        Assert.AreSequenceEqual(
            ["Command 'copy': Required argument 'destination' is missing.", "Command 'copy': Required argument 'hidden argument' is missing."],
            [.. missing.FormatDiagnostics()]);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-002")]
    public void Parse_ReportsUnsupportedModelsWithoutSelectingACommand()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--date", typeof(DateTime), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser().Parse(model, []);

        Assert.AreEqual(ParseOutcome.Invalid, result.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.InvalidModel, result.Diagnostics[0].Code);
        Assert.IsEmpty(result.SelectedPath);
        Assert.IsNull(result.Diagnostics[0].TokenIndex);
        Assert.AreEqual("--date", result.Diagnostics[0].DefinitionName);
        Assert.AreEqual("Option '--date' has an unsupported value type or converter.", result.Diagnostics[0].ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-002")]
    public void Parse_EnforcesConfiguredInputAndDiagnosticLimits()
    {
        CommandModel countLimitedModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumArgumentCount: 1));
        ParseResult tooManyArguments = new CliParser().Parse(countLimitedModel, ["one", "two"]);
        Assert.AreEqual(ParseDiagnosticCode.InputLimitExceeded, tooManyArguments.Diagnostics[0].Code);
        Assert.AreEqual(1, tooManyArguments.Diagnostics[0].TokenIndex);
        Assert.IsEmpty(tooManyArguments.SelectedPath);
        Assert.AreEqual("Argument 2: Too many arguments; the limit is 1.", tooManyArguments.Diagnostics[0].ToString());

        CommandModel tokenLimitedModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumTokenLength: 3));
        ParseResult longToken = new CliParser().Parse(tokenLimitedModel, ["long"]);
        Assert.AreEqual(ParseDiagnosticCode.InputLimitExceeded, longToken.Diagnostics[0].Code);
        Assert.AreEqual(0, longToken.Diagnostics[0].TokenIndex);
        Assert.AreEqual("Argument 1: This argument exceeds the limit of 3 UTF-16 code units.", longToken.Diagnostics[0].ToString());

        CommandModel totalLimitedModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumTotalCodeUnits: 3));
        ParseResult longVector = new CliParser().Parse(totalLimitedModel, ["ab", "cd"]);
        Assert.AreEqual(ParseDiagnosticCode.InputLimitExceeded, longVector.Diagnostics[0].Code);
        Assert.AreEqual(1, longVector.Diagnostics[0].TokenIndex);
        Assert.AreEqual("Argument 2: The combined argument length exceeds the limit of 3 UTF-16 code units.", longVector.Diagnostics[0].ToString());

        CommandModel diagnosticLimitedModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumDiagnostics: 2));
        ParseResult manyErrors = new CliParser().Parse(diagnosticLimitedModel, ["--bad", "--worse", "--worst"]);
        Assert.HasCount(2, manyErrors.Diagnostics);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, manyErrors.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.DiagnosticLimitExceeded, manyErrors.Diagnostics[1].Code);
        Assert.AreEqual("Too many errors; further diagnostics were omitted.", manyErrors.Diagnostics[1].ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-003")]
    public void Parse_FailsOnUnknownAndExcessInputButExposesItForInspection()
    {
        CommandModel optionModel = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar)));
        ParseResult unknownOption = new CliParser().Parse(optionModel, ["--wat"]);
        Assert.AreEqual(ParseOutcome.Invalid, unknownOption.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, unknownOption.Diagnostics[0].Code);
        Assert.AreEqual("--wat", unknownOption.UnmatchedTokens[0].Value);
        Assert.IsFalse(unknownOption.Diagnostics[0].Message.Contains("--wat", StringComparison.Ordinal));
        Assert.AreEqual("Argument 1: Unknown option.", unknownOption.Diagnostics[0].ToString());

        CommandModel positionalModel = CommandModel.Create(new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("name", typeof(string))));
        ParseResult excess = new CliParser().Parse(positionalModel, ["first", "secret"]);
        Assert.AreEqual(ParseDiagnosticCode.UnexpectedArgument, excess.Diagnostics[0].Code);
        Assert.AreEqual("secret", excess.UnmatchedTokens[0].Value);
        Assert.IsFalse(excess.Diagnostics[0].Message.Contains("secret", StringComparison.Ordinal));
        Assert.AreEqual("Argument 2: Unexpected positional argument \"secret\".", excess.Diagnostics[0].ToString());
        Assert.AreEqual("Argument 2: Unexpected positional argument.", excess.Diagnostics[0].ToString(includeArgumentText: false));
        Assert.AreSequenceEqual(["Argument 2: Unexpected positional argument."], [.. excess.FormatDiagnostics(includeArgumentText: false)]);

        ParseResult escaped = new CliParser().Parse(positionalModel, ["first", "extra\nline", new string('x', 128) + "truncated"]);
        Assert.AreSequenceEqual(
            ["Argument 2: Unexpected positional argument \"extra\\nline\".", $"Argument 3: Unexpected positional argument \"{new string('x', 128)}...\"."],
            [.. escaped.FormatDiagnostics()]);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-004")]
    public void Parse_SuggestsBoundedVisibleCanonicalNames()
    {
        CommandBuilder root = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy").AddAlias("cp"))
            .AddChild(new CommandBuilder("hidden") { IsHidden = true })
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--secret", typeof(string), OptionArity.RequiredScalar, isHidden: true));

        ParseResult commandTypo = new CliParser().Parse(CommandModel.Create(root), ["copi"]);
        ParseResult optionTypo = new CliParser().Parse(CommandModel.Create(root), ["--ouptut"]);
        ParseResult hiddenTypo = new CliParser().Parse(CommandModel.Create(root), ["--secrett"]);
        string longName = string.Concat("--", new string('x', 130));
        ParseResult overlongTypo = new CliParser().Parse(CommandModel.Create(root), [longName]);

        Assert.AreSequenceEqual(["copy"], [.. commandTypo.Diagnostics[0].Suggestions]);
        Assert.AreSequenceEqual(["--output"], [.. optionTypo.Diagnostics[0].Suggestions]);
        Assert.IsEmpty(hiddenTypo.Diagnostics[0].Suggestions);
        Assert.IsEmpty(overlongTypo.Diagnostics[0].Suggestions);
        Assert.AreEqual("Argument 1: Unknown command. Did you mean: copy?", commandTypo.Diagnostics[0].ToString());
        Assert.AreEqual("Argument 1: Unknown option. Did you mean: --output?", optionTypo.Diagnostics[0].ToString());
        Assert.AreEqual("Argument 1: Unknown option.", hiddenTypo.Diagnostics[0].ToString());

        CommandBuilder options = new("tool");
        for (int index = 0; index < 8; index++)
        {
            options.AddOption(new OptionDefinition(
                string.Create(CultureInfo.InvariantCulture, $"--option-{index}"),
                typeof(string),
                OptionArity.RequiredScalar));
        }

        ParseResult bounded = new CliParser().Parse(CommandModel.Create(options), ["--option-"]);
        Assert.HasCount(5, bounded.Diagnostics[0].Suggestions);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-003")]
    public void Parse_SelectsCanonicalCommandPathsAndHonorsAliasesAndPositionals()
    {
        CommandBuilder copy = new CommandBuilder("copy")
            .AddAlias("cp")
            .AddPositional(new PositionalDefinition("source", typeof(string), minimumValues: 0));
        CommandBuilder file = new CommandBuilder("file")
            .AddChild(copy)
            .AddPositional(new PositionalDefinition("argument", typeof(string)));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddChild(file));
        CliParser parser = new();

        ParseResult aliased = parser.Parse(model, ["file", "cp", "cp"]);
        ParseResult positionalTypo = parser.Parse(model, ["file", "cpoy"]);
        ParseResult unknownChild = parser.Parse(
            CommandModel.Create(new CommandBuilder("tool").AddChild(new CommandBuilder("copy"))),
            ["missing"]);

        Assert.AreEqual(ParseOutcome.Validated, aliased.Outcome);
        Assert.AreSequenceEqual(
            ["tool", "file", "copy"], [.. aliased.SelectedPath.Select(static command => command.Name)]);
        Assert.IsTrue(aliased.TryGetPositionalValue(
            aliased.SelectedCommand!,
            aliased.SelectedCommand!.Positionals[0],
            out string? source));
        Assert.AreEqual("cp", source);
        Assert.AreEqual(ParseOutcome.Validated, positionalTypo.Outcome);
        Assert.AreSequenceEqual(
            ["tool", "file"], [.. positionalTypo.SelectedPath.Select(static command => command.Name)]);
        Assert.AreEqual(ParseDiagnosticCode.UnknownCommand, unknownChild.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-03")]
    [TestProperty(Metadata.Requirement, "FEAT-03-003")]
    public void Parse_TracksConsumedCommandAndValueTokenPositions()
    {
        CommandBuilder copy = new CommandBuilder("copy")
            .AddOption(new OptionDefinition("--name", typeof(string), OptionArity.RequiredScalar))
            .AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string)));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddChild(copy));
        ParseResult selected = new CliParser().Parse(model, ["copy", "--name", "copy", "copy"]);

        CommandBuilder rootWithOption = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--name", typeof(string), OptionArity.RequiredScalar))
            .AddChild(new CommandBuilder("copy"));
        ParseResult optionBeforeChild = new CliParser().Parse(
            CommandModel.Create(rootWithOption),
            ["--name", "copy", "copy"]);

        Assert.AreEqual(ParseOutcome.Validated, selected.Outcome);
        Assert.IsTrue(selected.TryGetOptionValue(
            selected.SelectedCommand!,
            selected.SelectedCommand!.Options[0],
            out string? name));
        Assert.AreEqual("copy", name);
        Assert.IsTrue(selected.TryGetPositionalValue(
            selected.SelectedCommand,
            selected.SelectedCommand.Positionals[0],
            out IReadOnlyList<string>? paths));
        Assert.AreSequenceEqual(["copy"], [.. paths!]);
        Assert.AreSequenceEqual(
            ["tool", "copy"], [.. optionBeforeChild.SelectedPath.Select(static command => command.Name)]);
        Assert.AreEqual(ParseDiagnosticCode.OptionOutOfScope, optionBeforeChild.Diagnostics[0].Code);
        Assert.AreEqual(
            $"Argument 1: Option '{optionBeforeChild.Diagnostics[0].DefinitionName}' is not available for the selected command.",
            optionBeforeChild.Diagnostics[0].ToString());
        Assert.AreEqual(0, optionBeforeChild.Diagnostics[0].TokenIndex);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-12")]
    [TestProperty(Metadata.Requirement, "FEAT-12-001")]
    public void Parse_RecognizesInheritedOptionsOnlyInSelectedScope()
    {
        OptionDefinition verbose = new(
            "--verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            scope: OptionScope.Inherited);
        int regionValidatorCalls = 0;
        ValueValidator<string> regionValidator = new((_, _) =>
        {
            regionValidatorCalls++;
            return ValueValidationOutcome.Success;
        });
        OptionDefinition region = new(
            "--region",
            typeof(string),
            OptionArity.RequiredScalar,
            validators: [regionValidator],
            scope: OptionScope.Inherited);
        OptionDefinition rootOnly = new("--root-only", typeof(string), OptionArity.RequiredScalar);
        OptionDefinition leafOnly = new("--leaf-only", typeof(string), OptionArity.RequiredScalar);
        CommandBuilder leaf = new CommandBuilder("copy")
            .AddOption(leafOnly);
        CommandBuilder file = new CommandBuilder("file")
            .AddOption(region)
            .AddChild(leaf);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(verbose)
            .AddOption(rootOnly)
            .AddChild(file));
        CliParser parser = new();

        ParseResult beforePath = parser.Parse(model, ["--verbose", "file", "copy"]);
        ParseResult afterPath = parser.Parse(model, ["file", "copy", "--verbose"]);
        ParseResult betweenPath = parser.Parse(model, ["file", "--region=west", "copy"]);
        ParseResult ancestorBeforeOwner = parser.Parse(model, ["--region=west", "file", "copy"]);
        ParseResult localAfterChild = parser.Parse(model, ["file", "copy", "--root-only=x"]);
        ParseResult localBeforeChild = parser.Parse(model, ["--leaf-only=x", "file", "copy"]);
        ParseResult ownerLocalBeforeDescendant = parser.Parse(model, ["--root-only=x", "file", "copy"]);

        Assert.AreEqual(ParseOutcome.Validated, beforePath.Outcome);
        Assert.AreEqual(ParseOutcome.Validated, afterPath.Outcome);
        Assert.AreEqual(ParseOutcome.Validated, betweenPath.Outcome);
        Assert.IsTrue(beforePath.TryGetOptionValue(model.Root, verbose, out bool beforeValue));
        Assert.IsTrue(beforeValue);
        Assert.IsTrue(afterPath.TryGetOptionValue(verbose, out bool afterValue));
        Assert.IsTrue(afterValue);
        Assert.IsTrue(betweenPath.TryGetOptionValue(model.Root.Children[0], region, out string? regionValue));
        Assert.AreEqual("west", regionValue);
        ParsedValue regionEntry = betweenPath.Values.Single(value => ReferenceEquals(value.Option, region));
        Assert.AreSame(model.Root.Children[0], regionEntry.Owner);
        Assert.AreEqual(ValueOrigin.Explicit, regionEntry.Origin);
        Assert.AreEqual(1, regionValidatorCalls);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, ancestorBeforeOwner.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, localAfterChild.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, localBeforeChild.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.OptionOutOfScope, ownerLocalBeforeDescendant.Diagnostics[0].Code);
        Assert.AreSequenceEqual(
            ["tool", "file", "copy"], [.. ownerLocalBeforeDescendant.SelectedPath.Select(static command => command.Name)]);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-12")]
    [TestProperty(Metadata.Requirement, "FEAT-12-002")]
    public void Parse_BindsInheritedOptionsBeforeBetweenAndAfterSelectedPath()
    {
        OptionDefinition verbose = new(
            "--verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            scope: OptionScope.Inherited);
        OptionDefinition region = new(
            "--region",
            typeof(string),
            OptionArity.RequiredScalar,
            scope: OptionScope.Inherited);
        CommandBuilder root = new CommandBuilder("tool")
            .AddOption(verbose)
            .AddChild(new CommandBuilder("file")
                .AddOption(region)
                .AddChild(new CommandBuilder("copy")));
        CommandModel model = CommandModel.Create(root);
        CommandDefinition file = model.Root.Children[0];
        CliParser parser = new();

        ParseResult before = parser.Parse(model, ["--verbose", "file", "copy"]);
        ParseResult between = parser.Parse(model, ["file", "--region=west", "copy"]);
        ParseResult after = parser.Parse(model, ["file", "copy", "--verbose"]);

        Assert.AreEqual(ParseOutcome.Validated, before.Outcome);
        Assert.AreEqual(ParseOutcome.Validated, between.Outcome);
        Assert.AreEqual(ParseOutcome.Validated, after.Outcome);
        Assert.IsTrue(before.TryGetOptionValue(verbose, out bool beforeValue));
        Assert.IsTrue(beforeValue);
        Assert.IsTrue(after.TryGetOptionValue(model.Root, verbose, out bool afterValue));
        Assert.IsTrue(afterValue);
        Assert.IsTrue(between.TryGetOptionValue(file, region, out string? regionValue));
        Assert.AreEqual("west", regionValue);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-004")]
    public void Parse_HelpBypassesConversionAndRequirednessButNotStructuralErrors()
    {
        int converterCalls = 0;
        int validatorCalls = 0;
        int crossValidatorCalls = 0;
        int defaultFactoryCalls = 0;
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register((_, _) =>
            {
                converterCalls++;
                return ValueConversion.Failure<int>("integer.invalid");
            })
            .Build();
        ValueValidator<int> validator = new((_, _) =>
        {
            validatorCalls++;
            return ValueValidationOutcome.Success;
        });
        CommandBuilder root = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy")
                .AddOption(new OptionDefinition(
                    "--count",
                    typeof(int),
                    OptionArity.RequiredScalar,
                    isRequired: true,
                    validators: [validator]))
                .AddOption(new OptionDefinition(
                    "--label",
                    typeof(string),
                    OptionArity.RequiredScalar,
                    defaultValuePolicy: DefaultValuePolicy.Factory,
                    defaultValueFactory: () =>
                    {
                        defaultFactoryCalls++;
                        return "default";
                    }))
                .AddCrossValidator(_ =>
                {
                    crossValidatorCalls++;
                    return ValueValidationOutcome.Success;
                }));
        CommandModel model = CommandModel.Create(root, includeStandardHelp: true);
        CliParser parser = new(converters);

        ParseResult help = parser.Parse(model, ["copy", "--count=bad", "--help"]);
        ParseResult unknown = parser.Parse(model, ["copy", "--wat", "--help"]);
        ParseResult missingValue = parser.Parse(model, ["copy", "--count", "--help"]);
        ParseResult malformedHelp = parser.Parse(model, ["copy", "--help=maybe"]);
        ParseResult falseHelp = parser.Parse(model, ["copy", "--help", "false"]);
        ParseResult inlineFalseHelp = parser.Parse(model, ["copy", "--help=false"]);
        ParseResult repeatedHelp = parser.Parse(model, ["copy", "--help", "-h"]);
        ParseResult unknownCommand = parser.Parse(model, ["missing", "--help"]);

        Assert.AreEqual(ParseOutcome.HelpRequested, help.Outcome);
        Assert.AreSequenceEqual(["tool", "copy"], [.. help.SelectedPath.Select(static command => command.Name)]);
        Assert.IsEmpty(help.Values);
        Assert.AreEqual(0, converterCalls);
        Assert.AreEqual(0, validatorCalls);
        Assert.AreEqual(0, crossValidatorCalls);
        Assert.AreEqual(0, defaultFactoryCalls);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, unknown.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingValue, missingValue.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, malformedHelp.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, falseHelp.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, inlineFalseHelp.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.DuplicateOption, repeatedHelp.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.UnknownCommand, unknownCommand.Diagnostics[0].Code);

        CommandModel positionalModel = CommandModel.Create(
            new CommandBuilder("tool").AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string))),
            includeStandardHelp: true);
        ParseResult afterEndMarker = parser.Parse(positionalModel, ["--", "--help"]);
        Assert.AreEqual(ParseOutcome.Validated, afterEndMarker.Outcome);
        Assert.IsTrue(afterEndMarker.TryGetPositionalValue(
            positionalModel.Root,
            positionalModel.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        Assert.AreSequenceEqual(["--help"], [.. paths!]);
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
