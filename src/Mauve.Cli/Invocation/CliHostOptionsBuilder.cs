using System;
using System.Globalization;
using System.IO;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;

namespace Mauve.Cli.Invocation;

/// <summary>Builds reusable immutable host-option snapshots with constructor-equivalent defaults.</summary>
/// <remarks>Builders are not thread-safe. Writers and callback state remain caller-owned across snapshots.</remarks>
public sealed class CliHostOptionsBuilder
{
    private TextWriter? _standardOutput;
    private TextWriter? _standardError;
    private bool _includeArgumentTextInDiagnostics = true;
    private int _helpWidth = 80;
    private Func<HelpDocument, int, string>? _helpRenderer;
    private Func<HelpTreeDocument, string>? _helpTreeRenderer;
    private int _invalidInputExitCode = 2;
    private int _noHandlerExitCode = 1;
    private int _cancelledExitCode = 130;
    private string? _executableName;
    private ConverterRegistry? _converters;
    private CultureInfo? _numericCulture;
    private CliSyntaxOptions? _syntax;
    private bool _enableEnvironmentVariables;
    private string? _environmentVariablePrefix;
    private Func<string, string?>? _environmentVariableLookup;
    private char _environmentVariableDelimiter = ',';
    private bool _environmentVariablesEnabledByDefault = true;
    private readonly ExceptionMappingRegistryBuilder _exceptionMappings = new();
    private Func<CliExceptionRenderingContext, string>? _exceptionRenderer;

    /// <summary>Sets the help-output writer.</summary>
    /// <param name="writer">The caller-owned output writer.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithStandardOutput(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _standardOutput = writer;
        return this;
    }

    /// <summary>Sets the diagnostic and exception-output writer.</summary>
    /// <param name="writer">The caller-owned error writer.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithStandardError(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _standardError = writer;
        return this;
    }

    /// <summary>Controls whether host diagnostics include unexpected positional text.</summary>
    /// <param name="include">Whether to include escaped, bounded text; use false when output may contain secrets or be logged.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithArgumentTextInDiagnostics(bool include)
    {
        _includeArgumentTextInDiagnostics = include;
        return this;
    }

    /// <summary>Sets the help-rendering width, validated when building.</summary>
    /// <param name="width">The preferred width.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithHelpWidth(int width)
    {
        _helpWidth = width;
        return this;
    }

    /// <summary>Sets the renderer for sanitized help documents.</summary>
    /// <param name="renderer">The caller-owned renderer.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithHelpRenderer(Func<HelpDocument, int, string> renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _helpRenderer = renderer;
        return this;
    }

    /// <summary>Sets the renderer for sanitized help trees.</summary>
    /// <param name="renderer">The caller-owned renderer.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithHelpTreeRenderer(Func<HelpTreeDocument, string> renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _helpTreeRenderer = renderer;
        return this;
    }

    /// <summary>Sets the non-handler outcome codes without changing completed handler codes.</summary>
    /// <param name="invalidInput">The invalid-input code.</param>
    /// <param name="noHandler">The missing-handler code.</param>
    /// <param name="cancelled">The caller-cancellation code.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithExitCodes(int invalidInput = 2, int noHandler = 1, int cancelled = 130)
    {
        _invalidInputExitCode = invalidInput;
        _noHandlerExitCode = noHandler;
        _cancelledExitCode = cancelled;
        return this;
    }

    /// <summary>Sets the executable display name, validated when building.</summary>
    /// <param name="name">The display name used by help and exception rendering.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithExecutableName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _executableName = name;
        return this;
    }

    /// <summary>Sets the additional immutable host converter registry.</summary>
    /// <param name="converters">The host converters.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithConverters(ConverterRegistry converters)
    {
        ArgumentNullException.ThrowIfNull(converters);
        _converters = converters;
        return this;
    }

    /// <summary>Sets the numeric culture, copying it to prevent later caller mutation.</summary>
    /// <param name="culture">The explicit decimal-separator culture.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithNumericCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _numericCulture = CultureInfo.ReadOnly((CultureInfo)culture.Clone());
        return this;
    }

    /// <summary>Sets the immutable option-prefix configuration.</summary>
    /// <param name="syntax">The syntax configuration.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithSyntax(CliSyntaxOptions syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        _syntax = syntax;
        return this;
    }

    /// <summary>Configures environment binding, with constructor-equivalent validation at Build.</summary>
    /// <param name="enabled">Whether environment option binding is enabled.</param>
    /// <param name="prefix">The prefix, or null to derive it from the command root.</param>
    /// <param name="lookup">The lookup, or null to use the process environment.</param>
    /// <param name="delimiter">The collection CSV delimiter.</param>
    /// <param name="enabledByDefault">Whether options inheriting host policy are eligible.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithEnvironmentVariables(
        bool enabled = true,
        string? prefix = null,
        Func<string, string?>? lookup = null,
        char delimiter = ',',
        bool enabledByDefault = true)
    {
        _enableEnvironmentVariables = enabled;
        _environmentVariablePrefix = prefix;
        _environmentVariableLookup = lookup;
        _environmentVariableDelimiter = delimiter;
        _environmentVariablesEnabledByDefault = enabledByDefault;
        return this;
    }

    /// <summary>Adds immutable mappings, rejecting exact-type duplicates before changing this builder.</summary>
    /// <param name="mappings">The mappings to include.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder AddExceptionMappings(ExceptionMappingRegistry mappings)
    {
        _ = _exceptionMappings.Add(mappings);
        return this;
    }

    /// <summary>Maps an exception class and its subclasses to a constant safe message.</summary>
    /// <typeparam name="TException">The exception class to match.</typeparam>
    /// <param name="exitCode">The nonzero application exit code.</param>
    /// <param name="safeMessage">The explicitly safe public message.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder MapException<TException>(int exitCode, string safeMessage)
        where TException : Exception
    {
        _ = _exceptionMappings.Map<TException>(exitCode, safeMessage);
        return this;
    }

    /// <summary>Maps an exception class and its subclasses using a safe-message factory.</summary>
    /// <typeparam name="TException">The exception class to match.</typeparam>
    /// <param name="exitCode">The nonzero application exit code.</param>
    /// <param name="safeMessageFactory">The thread-safe, host-owned redaction callback.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder MapException<TException>(int exitCode, Func<TException, string> safeMessageFactory)
        where TException : Exception
    {
        _ = _exceptionMappings.Map(exitCode, safeMessageFactory);
        return this;
    }

    /// <summary>Replaces the mapped-exception renderer; the host owns redaction of original exception details.</summary>
    /// <param name="renderer">The renderer; empty output suppresses rendering and null output is rejected.</param>
    /// <returns>This builder.</returns>
    public CliHostOptionsBuilder WithExceptionRenderer(Func<CliExceptionRenderingContext, string> renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _exceptionRenderer = renderer;
        return this;
    }

    /// <summary>Validates configuration and creates a fresh immutable snapshot without consuming the builder.</summary>
    /// <returns>The host options; subsequent builder changes do not modify these settings.</returns>
    public CliHostOptions Build() => new(
        includeArgumentTextInDiagnostics: _includeArgumentTextInDiagnostics,
        standardOutput: _standardOutput,
        standardError: _standardError,
        helpWidth: _helpWidth,
        helpRenderer: _helpRenderer,
        invalidInputExitCode: _invalidInputExitCode,
        noHandlerExitCode: _noHandlerExitCode,
        cancelledExitCode: _cancelledExitCode,
        executableName: _executableName,
        helpTreeRenderer: _helpTreeRenderer,
        converters: _converters,
        numericCulture: _numericCulture,
        syntax: _syntax,
        enableEnvironmentVariables: _enableEnvironmentVariables,
        environmentVariablePrefix: _environmentVariablePrefix,
        environmentVariableLookup: _environmentVariableLookup,
        environmentVariableDelimiter: _environmentVariableDelimiter,
        environmentVariablesEnabledByDefault: _environmentVariablesEnabledByDefault,
        exceptionMappings: _exceptionMappings.Build(),
        exceptionRenderer: _exceptionRenderer);
}
