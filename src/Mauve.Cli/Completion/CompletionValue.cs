using System;

namespace Mauve.Cli.Completion;

/// <summary>A logical value suggested by an application completion provider.</summary>
public sealed class CompletionValue
{
    /// <summary>Creates a value candidate.</summary>
    /// <param name="insertionText">One logical value to insert.</param>
    /// <param name="displayText">Safe display text, defaulting to the insertion text.</param>
    /// <param name="description">Optional safe description.</param>
    public CompletionValue(string insertionText, string? displayText = null, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(insertionText);
        InsertionText = insertionText;
        DisplayText = displayText ?? insertionText;
        Description = description;
    }

    /// <summary>Gets the logical text to insert.</summary>
    public string InsertionText { get; }

    /// <summary>Gets the safe display text.</summary>
    public string DisplayText { get; }

    /// <summary>Gets the optional safe description.</summary>
    public string? Description { get; }
}
