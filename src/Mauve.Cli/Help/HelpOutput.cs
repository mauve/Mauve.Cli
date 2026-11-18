using System;

namespace Mauve.Cli.Help;

/// <summary>Writes help through an application-selected text renderer and sink.</summary>
public static class HelpOutput
{
    /// <summary>Renders and writes a document using an optional custom renderer.</summary>
    /// <param name="document">The sanitized help document.</param>
    /// <param name="sink">The application-selected output sink.</param>
    /// <param name="width">The preferred width passed to the renderer.</param>
    /// <param name="renderer">An optional renderer receiving sanitized help data.</param>
    public static void Write(
        HelpDocument document,
        Action<string> sink,
        int width = 80,
        Func<HelpDocument, int, string>? renderer = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sink);
        HelpTextRenderer.ValidateWidth(width);

        string output = renderer is null
            ? HelpTextRenderer.Render(document, width)
            : renderer(document, width)
                ?? throw new InvalidOperationException("The custom help renderer returned null.");
        sink(output);
    }
}
