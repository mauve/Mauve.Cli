using System;
using System.Collections.Generic;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Completion;

/// <summary>Defines an already-tokenized completion request.</summary>
public sealed class CompletionRequest
{
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
