using System;

namespace Mauve.Cli.Invocation;

/// <summary>Provides mapped failure data to a host-owned exception renderer.</summary>
/// <remarks>Custom renderers own redaction of the original exception, including its message, stack and inner exceptions.</remarks>
public sealed class CliExceptionRenderingContext
{
    internal CliExceptionRenderingContext(Exception exception, CliExceptionResult result, string executableName)
    {
        Exception = exception;
        Result = result;
        ExecutableName = executableName;
    }

    /// <summary>Gets the original exception, which may contain sensitive data and must not be rendered without redaction.</summary>
    public Exception Exception { get; }

    /// <summary>Gets the mapped nonzero exit code and safe public message.</summary>
    public CliExceptionResult Result { get; }

    /// <summary>Gets the host's executable display name.</summary>
    public string ExecutableName { get; }
}
