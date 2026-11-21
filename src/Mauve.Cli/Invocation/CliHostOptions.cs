using System;
using System.IO;
using Mauve.Cli.Help;

namespace Mauve.Cli.Invocation;

/// <summary>Configures output and exit-code policy for high-level CLI entry points.</summary>
public sealed class CliHostOptions
{
    /// <summary>Creates host settings, defaulting to console output and conventional exit codes.</summary>
    /// <param name="standardOutput">The sink for rendered help.</param>
    /// <param name="standardError">The sink for input and invocation diagnostics.</param>
    /// <param name="helpWidth">The preferred width passed to the help renderer.</param>
    /// <param name="helpRenderer">An optional custom renderer for sanitized help documents.</param>
    /// <param name="invalidInputExitCode">The returned code for malformed command input.</param>
    /// <param name="noHandlerExitCode">The returned code when the selected command has no handler.</param>
    /// <param name="cancelledExitCode">The returned code for caller cancellation.</param>
    public CliHostOptions(
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        int helpWidth = 80,
        Func<HelpDocument, int, string>? helpRenderer = null,
        int invalidInputExitCode = 2,
        int noHandlerExitCode = 1,
        int cancelledExitCode = 130)
    {
        HelpTextRenderer.ValidateWidth(helpWidth);
        StandardOutput = standardOutput ?? Console.Out;
        StandardError = standardError ?? Console.Error;
        HelpWidth = helpWidth;
        HelpRenderer = helpRenderer;
        InvalidInputExitCode = invalidInputExitCode;
        NoHandlerExitCode = noHandlerExitCode;
        CancelledExitCode = cancelledExitCode;
    }

    /// <summary>Gets the sink for rendered help.</summary>
    public TextWriter StandardOutput { get; }

    /// <summary>Gets the sink for input and invocation diagnostics.</summary>
    public TextWriter StandardError { get; }

    /// <summary>Gets the preferred help-rendering width.</summary>
    public int HelpWidth { get; }

    /// <summary>Gets the optional custom renderer for sanitized help documents.</summary>
    public Func<HelpDocument, int, string>? HelpRenderer { get; }

    /// <summary>Gets the exit code for malformed input.</summary>
    public int InvalidInputExitCode { get; }

    /// <summary>Gets the exit code when the selected command has no handler.</summary>
    public int NoHandlerExitCode { get; }

    /// <summary>Gets the exit code for caller cancellation.</summary>
    public int CancelledExitCode { get; }
}
