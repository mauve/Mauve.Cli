namespace Mauve.Cli.Parsing;

/// <summary>A caller-inspectable token that was not assigned to a definition.</summary>
public sealed class UnmatchedToken
{
    internal UnmatchedToken(int index, string value)
    {
        Index = index;
        Value = value;
    }

    /// <summary>Gets the zero-based argv index.</summary>
    public int Index { get; }

    /// <summary>Gets the original token text.</summary>
    public string Value { get; }
}
