using System;
using System.Globalization;
using System.IO;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;

namespace Mauve.Cli.Invocation;

/// <summary>Configures host output, parsing and high-level exception/exit-code policy.</summary>
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
    /// <param name="executableName">The executable display name used for help examples, defaulting to process argv zero.</param>
    /// <param name="helpTreeRenderer">An optional custom renderer for sanitized help trees.</param>
    /// <param name="converters">Additional host converters; exact model/host conflicts are rejected.</param>
    /// <param name="numericCulture">The optional explicit numeric decimal-separator culture.</param>
    /// <param name="syntax">The option prefixes.</param>
    /// <param name="enableEnvironmentVariables">Whether environment-variable option binding is enabled.</param>
    /// <param name="environmentVariablePrefix">The environment-variable prefix, defaulting to the normalized root command name.</param>
    /// <param name="environmentVariableLookup">An optional environment-variable lookup; defaults to the process environment.</param>
    /// <param name="environmentVariableDelimiter">The delimiter used by CSV-encoded collection values.</param>
    /// <param name="environmentVariablesEnabledByDefault">Whether options inheriting host policy are eligible for environment binding.</param>
    /// <param name="exceptionMappings">High-level host exception mappings; unmatched faults propagate unchanged.</param>
    /// <param name="exceptionRenderer">A custom mapped-exception renderer that owns redaction; defaults to the safe mapped message.</param>
    public CliHostOptions(
        TextWriter? standardOutput = null,
        TextWriter? standardError = null,
        int helpWidth = 80,
        Func<HelpDocument, int, string>? helpRenderer = null,
        int invalidInputExitCode = 2,
        int noHandlerExitCode = 1,
        int cancelledExitCode = 130,
        string? executableName = null,
        Func<HelpTreeDocument, string>? helpTreeRenderer = null,
        ConverterRegistry? converters = null,
        CultureInfo? numericCulture = null,
        CliSyntaxOptions? syntax = null,
        bool enableEnvironmentVariables = false,
        string? environmentVariablePrefix = null,
        Func<string, string?>? environmentVariableLookup = null,
        char environmentVariableDelimiter = ',',
        bool environmentVariablesEnabledByDefault = true,
        ExceptionMappingRegistry? exceptionMappings = null,
        Func<CliExceptionRenderingContext, string>? exceptionRenderer = null)
    {
        HelpTextRenderer.ValidateWidth(helpWidth);
        if (executableName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        }

        ExecutableName = executableName ?? Environment.GetCommandLineArgs()[0];
        StandardOutput = standardOutput ?? Console.Out;
        StandardError = standardError ?? Console.Error;
        HelpWidth = helpWidth;
        HelpRenderer = helpRenderer;
        HelpTreeRenderer = helpTreeRenderer;
        InvalidInputExitCode = invalidInputExitCode;
        NoHandlerExitCode = noHandlerExitCode;
        CancelledExitCode = cancelledExitCode;
        Converters = converters ?? ConverterRegistry.Empty();
        NumericCulture = ValueConverterEngine.CreateNumericCulture(numericCulture);
        Syntax = syntax ?? CliSyntaxOptions.Default;
        if (environmentVariablePrefix is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariablePrefix);
            if (environmentVariablePrefix.Contains('=', StringComparison.Ordinal)
                || environmentVariablePrefix.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("Environment-variable prefixes cannot contain '=' or null characters.", nameof(environmentVariablePrefix));
            }
        }

        if (environmentVariableDelimiter is '"' or '\r' or '\n' or '\0')
        {
            throw new ArgumentOutOfRangeException(nameof(environmentVariableDelimiter));
        }

        EnableEnvironmentVariables = enableEnvironmentVariables;
        EnvironmentVariablesEnabledByDefault = environmentVariablesEnabledByDefault;
        EnvironmentVariablePrefix = environmentVariablePrefix;
        EnvironmentVariableLookup = environmentVariableLookup ?? Environment.GetEnvironmentVariable;
        EnvironmentVariableDelimiter = environmentVariableDelimiter;
        ExceptionMappings = exceptionMappings ?? ExceptionMappingRegistry.Empty();
        ExceptionRenderer = exceptionRenderer ?? (static context => context.Result.SafeMessage);
    }

    /// <summary>Gets the sink for rendered help.</summary>
    public TextWriter StandardOutput { get; }

    /// <summary>Gets the sink for input and invocation diagnostics.</summary>
    public TextWriter StandardError { get; }

    /// <summary>Gets the preferred help-rendering width.</summary>
    public int HelpWidth { get; }

    /// <summary>Gets the optional custom renderer for sanitized help documents.</summary>
    public Func<HelpDocument, int, string>? HelpRenderer { get; }

    /// <summary>Gets the optional custom renderer for sanitized help trees.</summary>
    public Func<HelpTreeDocument, string>? HelpTreeRenderer { get; }

    /// <summary>Gets the exit code for malformed input.</summary>
    public int InvalidInputExitCode { get; }

    /// <summary>Gets the exit code when the selected command has no handler.</summary>
    public int NoHandlerExitCode { get; }

    /// <summary>Gets the exit code for caller cancellation.</summary>
    public int CancelledExitCode { get; }

    /// <summary>Gets the executable display name substituted for <c>$0</c> in help examples.</summary>
    public string ExecutableName { get; }

    /// <summary>Gets additional host-level converter registrations.</summary>
    public ConverterRegistry Converters { get; }

    /// <summary>Gets the frozen numeric culture used by high-level entry points.</summary>
    public CultureInfo NumericCulture { get; }

    /// <summary>Gets the option prefixes.</summary>
    public CliSyntaxOptions Syntax { get; }

    /// <summary>Gets whether environment-variable option binding is enabled for this invocation.</summary>
    public bool EnableEnvironmentVariables { get; }

    /// <summary>Gets whether options inherit environment-variable eligibility by default.</summary>
    public bool EnvironmentVariablesEnabledByDefault { get; }

    /// <summary>Gets the configured environment-variable prefix, or null to derive it from the root command.</summary>
    public string? EnvironmentVariablePrefix { get; }

    /// <summary>Gets the environment-variable lookup used for this invocation.</summary>
    public Func<string, string?> EnvironmentVariableLookup { get; }

    /// <summary>Gets the delimiter used by CSV-encoded collection values.</summary>
    public char EnvironmentVariableDelimiter { get; }

    /// <summary>Gets the exception mappings applied only by high-level Run and RunAsync hosts.</summary>
    public ExceptionMappingRegistry ExceptionMappings { get; }

    /// <summary>Gets the mapped-exception renderer; custom renderers own redaction of original exception details.</summary>
    /// <remarks>An empty result suppresses output. Null results and renderer faults propagate without remapping.</remarks>
    public Func<CliExceptionRenderingContext, string> ExceptionRenderer { get; }
}
