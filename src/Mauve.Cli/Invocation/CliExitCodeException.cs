using System;

namespace Mauve.Cli.Invocation;

/// <summary>Describes an application failure that high-level CLI hosts can render safely.</summary>
/// <param name="exitCode">The nonzero application exit code.</param>
/// <param name="safeMessage">The public message, excluding secrets and sensitive exception context.</param>
/// <param name="innerException">The original failure, retained for host inspection but not rendered by default.</param>
public sealed class CliExitCodeException(int exitCode, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    /// <summary>Creates a failure with exit code one and a generic safe message.</summary>
    public CliExitCodeException()
        : this(1, "The command failed.")
    {
    }

    /// <summary>Creates a failure with exit code one and an explicitly safe public message.</summary>
    /// <param name="message">The safe public message, excluding sensitive exception context.</param>
    public CliExitCodeException(string message)
        : this(1, message)
    {
    }

    /// <summary>Creates a failure with exit code one, a safe message and the original exception.</summary>
    /// <param name="message">The safe public message, excluding sensitive exception context.</param>
    /// <param name="innerException">The original exception, not rendered by default.</param>
    public CliExitCodeException(string message, Exception? innerException)
        : this(1, message, innerException)
    {
    }

    /// <summary>Gets the nonzero application exit code.</summary>
    public int ExitCode => Result.ExitCode;

    /// <summary>Gets the explicitly safe public message.</summary>
    public string SafeMessage => Result.SafeMessage;

    internal CliExceptionResult Result { get; } = new(exitCode, safeMessage);
}
