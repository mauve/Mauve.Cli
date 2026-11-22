using System;

namespace Mauve.Cli.Help;

/// <summary>Writes a sanitized help tree through a renderer and sink.</summary>
public static class HelpTreeOutput
{
    /// <summary>Renders and writes a tree using an optional custom renderer.</summary>
    /// <param name="document">The sanitized help-tree projection.</param>
    /// <param name="sink">The application-selected output sink.</param>
    /// <param name="renderer">An optional custom renderer.</param>
    public static void Write(
        HelpTreeDocument document,
        Action<string> sink,
        Func<HelpTreeDocument, string>? renderer = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sink);
        string output = renderer is null
            ? HelpTreeRenderer.Render(document)
            : renderer(document) ?? throw new InvalidOperationException("The custom help-tree renderer returned null.");
        sink(output);
    }
}
