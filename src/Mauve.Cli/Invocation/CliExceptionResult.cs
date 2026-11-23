using System;

namespace Mauve.Cli.Invocation;

/// <summary>Contains a failure exit code and an explicitly safe public message.</summary>
public sealed class CliExceptionResult
{
    /// <summary>Creates a mapped failure without exposing the original exception's details.</summary>
    /// <param name="exitCode">The nonzero application exit code, preserved without platform-specific normalization.</param>
    /// <param name="safeMessage">The public message, excluding secrets and sensitive exception context.</param>
    public CliExceptionResult(int exitCode, string safeMessage)
    {
        if (exitCode == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exitCode), "An exception cannot report a successful exit code.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        ExitCode = exitCode;
        SafeMessage = safeMessage;
    }

    /// <summary>Gets the nonzero application exit code.</summary>
    public int ExitCode { get; }

    /// <summary>Gets the explicitly safe public message.</summary>
    public string SafeMessage { get; }
}
