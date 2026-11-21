using System;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Invocation;

/// <summary>Provides the validated values and selected command for one directive occurrence.</summary>
public sealed class DirectiveInvocationContext
{
    internal DirectiveInvocationContext(ParsedDirective directive, ParseResult parseResult)
    {
        Directive = directive;
        ParseResult = parseResult;
        CommandContext = new CommandInvocationContext(
            parseResult.SelectedCommand
                ?? throw new InvalidOperationException("The directive parse has no selected command."),
            parseResult);
    }

    /// <summary>Gets the directive occurrence.</summary>
    public ParsedDirective Directive { get; }

    /// <summary>Gets the registered directive definition.</summary>
    public DirectiveDefinition Definition => Directive.Definition;

    /// <summary>Gets the complete parse result.</summary>
    public ParseResult ParseResult { get; }

    /// <summary>Gets the invocation context for the selected command and current parse.</summary>
    public CommandInvocationContext CommandContext { get; }

    /// <summary>Attempts to get one typed value from this directive occurrence.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="definition">The value definition.</param>
    /// <param name="value">The typed value.</param>
    /// <returns>True when the value is available.</returns>
    public bool TryGetValue<T>(DirectiveValueDefinition definition, out T? value)
        => Directive.TryGetValue(definition, out value);
}
