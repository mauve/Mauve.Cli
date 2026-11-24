using System;
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
}
