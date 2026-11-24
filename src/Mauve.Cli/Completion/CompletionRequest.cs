using System;
using System.Collections.Generic;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Completion;

/// <summary>Defines an already-tokenized completion request.</summary>
public sealed class CompletionRequest
{
    /// <summary>Parses a bounded raw command line into a completion request.</summary>
    /// <param name="model">The validated command model that supplies input limits.</param>
    /// <param name="commandLine">The raw command line, including its executable token.</param>
    /// <param name="cursor">The cursor offset in UTF-16 code units.</param>
    /// <returns>A request whose arguments omit the executable token.</returns>
    /// <remarks>
    /// This parser recognizes whitespace-separated tokens, single and double quotes, and backslash
    /// escapes; it does not implement a complete shell grammar or perform expansion. Matching quote
    /// characters delimit quoted text and unmatched quotes are tolerated. Outside single quotes, a
    /// backslash escapes the following character; inside double quotes it escapes only a quote or
    /// backslash. Quote delimiters are removed and escaped characters become part of the decoded
    /// token. Cursor positions, raw-line length, and decoded token lengths use UTF-16 code units.
    /// The raw line may not exceed <see cref="CliLimits.MaximumTotalCodeUnits"/>; each decoded
    /// token, including the executable, may not exceed <see cref="CliLimits.MaximumTokenLength"/>;
    /// and the returned arguments may not exceed <see cref="CliLimits.MaximumArgumentCount"/>.
    ///
    /// The line must contain an executable token, and the cursor must be strictly after that token's
    /// end while remaining between zero and the raw line's length. A cursor in whitespace after the
    /// executable selects an empty argument; text after the cursor is retained.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="commandLine"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cursor"/> is outside the raw line.</exception>
    /// <exception cref="ArgumentException">
    /// The line has no executable token, the cursor is not strictly after the executable token, or
    /// the line exceeds a configured input limit.
    /// </exception>
    public static CompletionRequest ParseCommandLine(CommandModel model, string commandLine, int cursor)
        => CompletionCommandLine.CreateRequest(model, commandLine, cursor);

    /// <summary>Creates a request using a UTF-16 cursor offset in an argv token.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="arguments">Logical argument tokens without the executable name.</param>
    /// <param name="cursorTokenIndex">The active token index, or the token count to append an empty token.</param>
    /// <param name="cursorCharacterOffset">The UTF-16 offset in the active token.</param>
    public CompletionRequest(
        CommandModel model,
        IReadOnlyList<string> arguments,
        int cursorTokenIndex,
        int cursorCharacterOffset)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(arguments);
        Model = model;
        Arguments = arguments;
        CursorTokenIndex = cursorTokenIndex;
        CursorCharacterOffset = cursorCharacterOffset;
    }

    /// <summary>Gets the validated command model.</summary>
    public CommandModel Model { get; }

    /// <summary>Gets the logical argument vector.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Gets the active token index.</summary>
    public int CursorTokenIndex { get; }

    /// <summary>Gets the UTF-16 cursor offset in the active token.</summary>
    public int CursorCharacterOffset { get; }
}
