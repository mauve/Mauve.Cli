using System;
using System.Collections.Generic;
using System.Text;
using Mauve.Cli.Completion;
using Mauve.Cli.Definitions;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Completion;

[TestClass]
public sealed class CompletionCommandLineTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-005")]
    public void ParseCommandLine_MapsQuotedEscapedAndWhitespacePositions()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));
        string payload = "tool copy \"hello world\" a\\ b '' tail";

        CompletionRequest quoted = CompletionRequest.ParseCommandLine(model, payload, 17);
        CompletionRequest escaped = CompletionRequest.ParseCommandLine(model, payload, 27);
        CompletionRequest empty = CompletionRequest.ParseCommandLine(model, payload, 30);
        CompletionRequest between = CompletionRequest.ParseCommandLine(model, payload, 24);
        CompletionRequest append = CompletionRequest.ParseCommandLine(model, "tool ", 5);

        Assert.AreSequenceEqual(["copy", "hello world", "a b", string.Empty, "tail"], quoted.Arguments);
        Assert.AreEqual(1, quoted.CursorTokenIndex);
        Assert.AreEqual(6, quoted.CursorCharacterOffset);
        Assert.AreEqual(2, escaped.CursorTokenIndex);
        Assert.AreEqual(2, escaped.CursorCharacterOffset);
        Assert.AreEqual(3, empty.CursorTokenIndex);
        Assert.AreEqual(0, empty.CursorCharacterOffset);
        Assert.AreEqual(2, between.CursorTokenIndex);
        Assert.AreEqual(0, between.CursorCharacterOffset);
        Assert.AreEqual(0, append.CursorTokenIndex);
        Assert.AreEqual(0, append.CursorCharacterOffset);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-005")]
    public void ParseCommandLine_TreatsPayloadAsTextAndRejectsInvalidCoordinates()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));
        string payload = "tool '$(echo x)' \"unfinished";
        CompletionRequest request = CompletionRequest.ParseCommandLine(model, payload, payload.Length);

        Assert.AreSequenceEqual(["$(echo x)", "unfinished"], request.Arguments);
        Assert.AreEqual(1, request.CursorTokenIndex);
        Assert.AreEqual("unfinished".Length, request.CursorCharacterOffset);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompletionRequest.ParseCommandLine(model, payload, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CompletionRequest.ParseCommandLine(model, payload, payload.Length + 1));
        Assert.ThrowsExactly<ArgumentException>(() => CompletionRequest.ParseCommandLine(model, payload, 2));
        Assert.ThrowsExactly<ArgumentException>(() => CompletionRequest.ParseCommandLine(model, string.Empty, 0));
        Assert.ThrowsExactly<ArgumentException>(() => CompletionRequest.ParseCommandLine(model, "tool", 4));
        Assert.ThrowsExactly<ArgumentNullException>(() => CompletionRequest.ParseCommandLine(null!, payload, 0));
        Assert.ThrowsExactly<ArgumentNullException>(() => CompletionRequest.ParseCommandLine(model, null!, 0));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-005")]
    public void ParseCommandLine_UsesUtf16OffsetsAndModelLimits()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));
        CompletionRequest request = CompletionRequest.ParseCommandLine(model, "tool 😀value", 7);

        Assert.AreEqual(0, request.CursorTokenIndex);
        Assert.AreEqual(2, request.CursorCharacterOffset);

        CommandModel totalLimitModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumTotalCodeUnits: 5));
        Assert.ThrowsExactly<ArgumentException>(
            () => CompletionRequest.ParseCommandLine(totalLimitModel, "tool x", 6));

        CommandModel tokenLimitModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumTokenLength: 3));
        Assert.ThrowsExactly<ArgumentException>(
            () => CompletionRequest.ParseCommandLine(tokenLimitModel, "t long", 6));

        CommandModel argumentLimitModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumArgumentCount: 1));
        Assert.ThrowsExactly<ArgumentException>(
            () => CompletionRequest.ParseCommandLine(argumentLimitModel, "tool one two", 12));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-005")]
    public void ParseCommandLine_MapsEveryCursorInGeneratedSupportedEncodings()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));

        foreach (PropertyInputs.GeneratedCase<PropertyInputs.CompletionToken> generated
            in PropertyInputs.CompletionTokenEncodings())
        {
            string encodedToken = generated.Value.EncodedToken.Length == 0
                ? "''"
                : generated.Value.EncodedToken;
            string text = "tool " + encodedToken;
            for (int cursor = 0; cursor <= text.Length; cursor++)
            {
                string message = $"{generated.AssertionMessage}, cursor UTF-16 offset {cursor}";
                if (cursor <= "tool".Length)
                {
                    Assert.ThrowsExactly<ArgumentException>(
                        () => CompletionRequest.ParseCommandLine(model, text, cursor),
                        message);
                    continue;
                }

                ExpectedCursor expected = DecodeExpectedCursor(text, cursor);
                CompletionRequest request = CompletionRequest.ParseCommandLine(model, text, cursor);
                AssertRequestMatchesExpected(request, expected, message);
                Assert.HasCount(1, request.Arguments, message);
                Assert.AreEqual(generated.Value.LogicalToken, request.Arguments[0], message);
            }
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-005")]
    public void ParseCommandLine_FixedGrammarExamplesPreserveFragmentsAndBackslashes()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));
        (string Text, string[] Arguments)[] examples =
        [
            ("tool ab\" c \"de 'x'\\''y' \"\" tail", ["ab c de", "x'y", string.Empty, "tail"]),
            ("""tool one\\\\two "quote: \"x\" and \\ slash" """, ["""one\\two""", """quote: "x" and \ slash"""]),
            ("tool 'unfinished value", ["unfinished value"]),
            ("tool plain\\ word next", ["plain word", "next"])
        ];

        foreach ((string text, string[] expectedArguments) in examples)
        {
            for (int cursor = 0; cursor <= text.Length; cursor++)
            {
                if (cursor <= "tool".Length)
                {
                    Assert.ThrowsExactly<ArgumentException>(
                        () => CompletionRequest.ParseCommandLine(model, text, cursor),
                        $"Fixed example {text}, cursor UTF-16 offset {cursor}");
                    continue;
                }

                ExpectedCursor expected = DecodeExpectedCursor(text, cursor);
                CompletionRequest request = CompletionRequest.ParseCommandLine(model, text, cursor);
                string message = $"Fixed example {text}, cursor UTF-16 offset {cursor}";
                AssertRequestMatchesExpected(request, expected, message);
                Assert.HasCount(expectedArguments.Length, request.Arguments, message);
                for (int index = 0; index < expectedArguments.Length; index++)
                {
                    Assert.AreEqual(expectedArguments[index], request.Arguments[index], message);
                }
            }
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-005")]
    public void ParseCommandLine_EquivalentEncodingsMapToSameLogicalTokenAndRetainSuffix()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));
        string[] equivalent =
        [
            """tool two\ words""",
            "tool \"two words\"",
            """tool 'two words'"""
        ];

        foreach (string text in equivalent)
        {
            CompletionRequest request = CompletionRequest.ParseCommandLine(model, text, text.Length);
            Assert.AreSequenceEqual(["two words"], request.Arguments);
            Assert.AreEqual(0, request.CursorTokenIndex);
            Assert.AreEqual("two words".Length, request.CursorCharacterOffset);
        }

        string complete = "tool --mode=fa";
        string withSuffix = "tool --mode=fast";
        CompletionRequest noSuffix = CompletionRequest.ParseCommandLine(model, complete, complete.Length);
        CompletionRequest retainedSuffix = CompletionRequest.ParseCommandLine(
            model,
            withSuffix,
            withSuffix.Length - "st".Length);

        Assert.AreEqual(noSuffix.CursorTokenIndex, retainedSuffix.CursorTokenIndex);
        Assert.AreEqual(noSuffix.CursorCharacterOffset, retainedSuffix.CursorCharacterOffset);
        Assert.AreEqual("--mode=fa", noSuffix.Arguments[0]);
        Assert.AreEqual("--mode=fast", retainedSuffix.Arguments[0]);
        Assert.AreNotEqual(noSuffix.Arguments[0], retainedSuffix.Arguments[0]);

        const string fragmentedSuffix = """tool pre" fixSUFFIX" tail""";
        int cursorBeforeSuffix = fragmentedSuffix.IndexOf("SUFFIX", StringComparison.Ordinal);
        CompletionRequest fragmented = CompletionRequest.ParseCommandLine(
            model,
            fragmentedSuffix,
            cursorBeforeSuffix);
        Assert.AreSequenceEqual(["pre fixSUFFIX", "tail"], fragmented.Arguments);
        Assert.AreEqual("pre fix".Length, fragmented.CursorCharacterOffset);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-005")]
    public void ParseCommandLine_AcceptsExactInputLimitsAndRejectsOutOfRangeCursors()
    {
        CommandModel totalLimitModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumTotalCodeUnits: 6));
        CompletionRequest totalLimitRequest = CompletionRequest.ParseCommandLine(totalLimitModel, "tool x", 6);
        Assert.AreSequenceEqual(["x"], totalLimitRequest.Arguments);

        CommandModel tokenLimitModel = CommandModel.Create(
            new CommandBuilder("t"),
            new CliLimits(maximumTokenLength: 3));
        CompletionRequest tokenLimitRequest = CompletionRequest.ParseCommandLine(tokenLimitModel, "t abc", 5);
        Assert.AreSequenceEqual(["abc"], tokenLimitRequest.Arguments);

        CommandModel argumentLimitModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumArgumentCount: 1));
        CompletionRequest argumentLimitRequest = CompletionRequest.ParseCommandLine(
            argumentLimitModel,
            "tool one",
            8);
        Assert.AreSequenceEqual(["one"], argumentLimitRequest.Arguments);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => CompletionRequest.ParseCommandLine(totalLimitModel, "tool x", -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => CompletionRequest.ParseCommandLine(totalLimitModel, "tool x", 7));
    }

    private static void AssertRequestMatchesExpected(
        CompletionRequest request,
        ExpectedCursor expected,
        string message)
    {
        Assert.HasCount(expected.Arguments.Count, request.Arguments, message);
        for (int index = 0; index < expected.Arguments.Count; index++)
        {
            Assert.AreEqual(expected.Arguments[index], request.Arguments[index], message);
        }

        Assert.IsTrue(
            request.CursorTokenIndex >= 0 && request.CursorTokenIndex <= request.Arguments.Count,
            message);
        Assert.AreEqual(expected.CursorTokenIndex, request.CursorTokenIndex, message);
        Assert.AreEqual(expected.CursorCharacterOffset, request.CursorCharacterOffset, message);
        if (request.CursorTokenIndex == request.Arguments.Count)
        {
            Assert.AreEqual(0, request.CursorCharacterOffset, message);
        }
        else
        {
            Assert.IsTrue(
                request.CursorCharacterOffset >= 0
                    && request.CursorCharacterOffset <= request.Arguments[request.CursorTokenIndex].Length,
                message);
        }
    }

    private static ExpectedCursor DecodeExpectedCursor(string text, int cursor)
    {
        List<ExpectedToken> tokens = DecodeExpectedTokens(text);
        List<string> arguments = [];
        for (int index = 1; index < tokens.Count; index++)
        {
            arguments.Add(tokens[index].Value);
        }

        for (int index = 1; index < tokens.Count; index++)
        {
            ExpectedToken token = tokens[index];
            if (token.Start <= cursor && cursor <= token.End)
            {
                int decodedOffset = 0;
                foreach (int sourceEnd in token.DecodedSourceEnds)
                {
                    if (sourceEnd <= cursor)
                    {
                        decodedOffset++;
                    }
                }

                return new ExpectedCursor(arguments, index - 1, decodedOffset);
            }
        }

        int insertionIndex = 0;
        for (int index = 1; index < tokens.Count && tokens[index].End < cursor; index++)
        {
            insertionIndex++;
        }

        return new ExpectedCursor(arguments, insertionIndex, 0);
    }

    private static List<ExpectedToken> DecodeExpectedTokens(string text)
    {
        List<ExpectedToken> tokens = [];
        int index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            int start = index;
            char quote = '\0';
            StringBuilder value = new();
            List<int> decodedSourceEnds = [];
            while (index < text.Length)
            {
                char character = text[index];
                if (quote == '\0' && char.IsWhiteSpace(character))
                {
                    break;
                }

                if ((character is '\'' or '"') && (quote == '\0' || quote == character))
                {
                    quote = quote == '\0' ? character : '\0';
                    index++;
                }
                else if (character == '\\'
                    && quote != '\''
                    && index + 1 < text.Length
                    && (quote == '\0' || text[index + 1] is '\\' or '"'))
                {
                    value.Append(text[index + 1]);
                    index += 2;
                    decodedSourceEnds.Add(index);
                }
                else
                {
                    value.Append(character);
                    index++;
                    decodedSourceEnds.Add(index);
                }
            }

            tokens.Add(new ExpectedToken(start, index, value.ToString(), decodedSourceEnds));
        }

        return tokens;
    }

    private sealed record ExpectedToken(
        int Start,
        int End,
        string Value,
        IReadOnlyList<int> DecodedSourceEnds);

    private sealed record ExpectedCursor(
        IReadOnlyList<string> Arguments,
        int CursorTokenIndex,
        int CursorCharacterOffset);
}
