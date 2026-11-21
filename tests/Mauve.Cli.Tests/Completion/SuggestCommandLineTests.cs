using System;
using Mauve.Cli.Completion;
using Mauve.Cli.Definitions;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Completion;

[TestClass]
public sealed class SuggestCommandLineTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-005")]
    public void CreateRequest_MapsQuotedEscapedAndWhitespacePositions()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));
        string payload = "tool copy \"hello world\" a\\ b '' tail";

        CompletionRequest quoted = SuggestCommandLine.CreateRequest(model, payload, 17);
        CompletionRequest escaped = SuggestCommandLine.CreateRequest(model, payload, 27);
        CompletionRequest empty = SuggestCommandLine.CreateRequest(model, payload, 30);
        CompletionRequest between = SuggestCommandLine.CreateRequest(model, payload, 24);
        CompletionRequest append = SuggestCommandLine.CreateRequest(model, "tool ", 5);

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
    public void CreateRequest_TreatsPayloadAsTextAndRejectsInvalidCoordinates()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));
        string payload = "tool '$(echo x)' \"unfinished";
        CompletionRequest request = SuggestCommandLine.CreateRequest(model, payload, payload.Length);

        Assert.AreSequenceEqual(["$(echo x)", "unfinished"], request.Arguments);
        Assert.AreEqual(1, request.CursorTokenIndex);
        Assert.AreEqual("unfinished".Length, request.CursorCharacterOffset);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SuggestCommandLine.CreateRequest(model, payload, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SuggestCommandLine.CreateRequest(model, payload, payload.Length + 1));
        Assert.ThrowsExactly<ArgumentException>(() => SuggestCommandLine.CreateRequest(model, payload, 2));
        Assert.ThrowsExactly<ArgumentException>(() => SuggestCommandLine.CreateRequest(model, string.Empty, 0));
    }
}
