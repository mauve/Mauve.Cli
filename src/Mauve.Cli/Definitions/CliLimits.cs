using System;

namespace Mauve.Cli.Definitions;

/// <summary>Configures bounded model and argument processing limits.</summary>
/// <param name="maximumArgumentCount">Maximum number of argv tokens.</param>
/// <param name="maximumTokenLength">Maximum UTF-16 code units in one token.</param>
/// <param name="maximumTotalCodeUnits">Maximum UTF-16 code units across all tokens.</param>
/// <param name="maximumCommandDepth">Maximum command nesting, including the root.</param>
/// <param name="maximumOptionsPerCommand">Maximum local options on one command.</param>
/// <param name="maximumModelNodes">Maximum command, option and positional nodes.</param>
/// <param name="maximumDiagnostics">Maximum diagnostics returned by one parse.</param>
public sealed class CliLimits(
    int maximumArgumentCount = 4096,
    int maximumTokenLength = 16384,
    long maximumTotalCodeUnits = 1048576,
    int maximumCommandDepth = 64,
    int maximumOptionsPerCommand = 4096,
    int maximumModelNodes = 10000,
    int maximumDiagnostics = 64)
{
    /// <summary>Gets the default safety limits.</summary>
    public static CliLimits Default { get; } = new();

    /// <summary>Gets the maximum number of argv tokens.</summary>
    public int MaximumArgumentCount { get; } = ValidatePositive(maximumArgumentCount, nameof(maximumArgumentCount));

    /// <summary>Gets the maximum UTF-16 code units in one token.</summary>
    public int MaximumTokenLength { get; } = ValidatePositive(maximumTokenLength, nameof(maximumTokenLength));

    /// <summary>Gets the maximum UTF-16 code units across all tokens.</summary>
    public long MaximumTotalCodeUnits { get; } = maximumTotalCodeUnits > 0
            ? maximumTotalCodeUnits
            : throw new ArgumentOutOfRangeException(nameof(maximumTotalCodeUnits));

    /// <summary>Gets the maximum command nesting, including the root.</summary>
    public int MaximumCommandDepth { get; } = ValidatePositive(maximumCommandDepth, nameof(maximumCommandDepth));

    /// <summary>Gets the maximum local options on one command.</summary>
    public int MaximumOptionsPerCommand { get; } = ValidatePositive(maximumOptionsPerCommand, nameof(maximumOptionsPerCommand));

    /// <summary>Gets the maximum command, option and positional nodes.</summary>
    public int MaximumModelNodes { get; } = ValidatePositive(maximumModelNodes, nameof(maximumModelNodes));

    /// <summary>Gets the maximum diagnostics returned by one parse.</summary>
    public int MaximumDiagnostics { get; } = ValidatePositive(maximumDiagnostics, nameof(maximumDiagnostics));

    private static int ValidatePositive(int value, string parameterName)
    {
        return value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName);
    }
}
