using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Definitions;

/// <summary>Builds a command graph that can be validated into an immutable model.</summary>
public sealed class CommandBuilder
{
    private readonly List<string> _aliases = [];
    private readonly List<string> _examples = [];
    private readonly List<CommandBuilder> _children = [];
    private readonly List<DirectiveBuilder> _directives = [];
    private readonly List<OptionDefinition> _options = [];
    private readonly List<OptionPackInclusionBuilder> _packs = [];
    private readonly List<PositionalDefinition> _positionals = [];
    private readonly List<CrossValueValidator> _crossValidators = [];
    private readonly ConverterRegistryBuilder _converters = new();

    /// <summary>Registers a model-wide converter on the command root.</summary>
    /// <typeparam name="T">The converter target type.</typeparam>
    /// <param name="converter">The application callback.</param>
    /// <param name="completionSafe">Whether completion may invoke this callback.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder RegisterConverter<T>(ValueConverter<T> converter, bool completionSafe = false)
    {
        _ = completionSafe ? _converters.RegisterCompletionSafe(converter) : _converters.Register(converter);
        HasConverters = true;
        return this;
    }

    /// <summary>Registers a statically referenced provider on the model root.</summary>
    /// <typeparam name="T">The converter target type.</typeparam>
    /// <typeparam name="TConverter">The provider type.</typeparam>
    /// <param name="completionSafe">Whether completion may invoke this provider.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder RegisterConverter<T, TConverter>(bool completionSafe = false)
        where TConverter : ICliValueConverter<T>
        => RegisterConverter(TConverter.Convert, completionSafe);

    /// <summary>Adds an immutable registry to the model root, rejecting duplicates.</summary>
    /// <param name="converters">The model-wide registry.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddConverters(ConverterRegistry converters)
    {
        _ = _converters.Add(converters);
        HasConverters = true;
        return this;
    }

    /// <summary>Registers explicit ordered union case constructors on the model root.</summary>
    /// <typeparam name="TUnion">The union type.</typeparam>
    /// <param name="configure">Declares the ordered cases.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder RegisterUnion<TUnion>(Action<UnionConverterBuilder<TUnion>> configure)
        where TUnion : IUnion
    {
        _ = _converters.RegisterUnion(configure);
        HasConverters = true;
        return this;
    }

    /// <summary>Creates a command builder.</summary>
    /// <param name="name">The canonical name or root display label.</param>
    public CommandBuilder(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets the canonical name or root display label.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the short description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the long description.</summary>
    public string? LongDescription { get; set; }

    /// <summary>Gets or sets whether ordinary help and completion hide this command.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Adds an ordered, single-line shell invocation example.</summary>
    /// <param name="example">Inert example text, optionally containing a standalone executable placeholder <c>$0</c>.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddExample(string example)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(example);
        if (example.Contains('\r', StringComparison.Ordinal) || example.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("A command example must contain one invocation line.", nameof(example));
        }

        _examples.Add(example.Trim());
        return this;
    }

    /// <summary>Adds an alias to this command.</summary>
    /// <param name="alias">The command alias.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        _aliases.Add(alias);
        return this;
    }

    /// <summary>Adds a child command.</summary>
    /// <param name="child">The child builder.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddChild(CommandBuilder child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
        return this;
    }

    /// <summary>Adds an option definition.</summary>
    /// <param name="option">The option definition.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddOption(OptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);
        _options.Add(option);
        return this;
    }

    /// <summary>Adds a directive available across the command tree.</summary>
    /// <param name="directive">The directive registration.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddDirective(DirectiveBuilder directive)
    {
        ArgumentNullException.ThrowIfNull(directive);
        _directives.Add(directive);
        return this;
    }

    /// <summary>Adds a reusable option pack with an explicit identity key.</summary>
    /// <param name="key">The inclusion key, unique among this command's packs.</param>
    /// <param name="pack">The pack builder.</param>
    /// <param name="title">An optional help-group title.</param>
    /// <param name="description">An optional help-group description.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddPack(
        string key,
        OptionPackBuilder pack,
        string? title = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(pack);
        _packs.Add(new OptionPackInclusionBuilder(key, pack, title, description));
        return this;
    }

    /// <summary>Adds a positional argument definition.</summary>
    /// <param name="positional">The positional argument definition.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddPositional(PositionalDefinition positional)
    {
        ArgumentNullException.ThrowIfNull(positional);
        _positionals.Add(positional);
        return this;
    }

    /// <summary>Adds an ordered cross-value validator for this command.</summary>
    /// <param name="validator">The validator callback.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddCrossValidator(CrossValueValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _crossValidators.Add(validator);
        return this;
    }

    /// <summary>Registers a synchronous handler with no application exit code.</summary>
    /// <param name="handler">The handler to invoke after successful validation.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder SetHandler(Action<CommandInvocationContext, CancellationToken> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore((context, cancellationToken) =>
        {
            handler(context, cancellationToken);
            return new ValueTask<int>(0);
        });
        return this;
    }

    /// <summary>Registers a synchronous handler that returns an application exit code.</summary>
    /// <param name="handler">The handler to invoke after successful validation.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder SetHandler(Func<CommandInvocationContext, CancellationToken, int> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore((context, cancellationToken) =>
            new ValueTask<int>(handler(context, cancellationToken)));
        return this;
    }

    /// <summary>Registers an asynchronous handler with no application exit code.</summary>
    /// <param name="handler">The handler to invoke after successful validation.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder SetTaskHandler(Func<CommandInvocationContext, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore(async (context, cancellationToken) =>
        {
            Task task = handler(context, cancellationToken)
                ?? throw new InvalidOperationException("The asynchronous handler returned a null task.");
            await task.ConfigureAwait(false);
            return 0;
        });
        return this;
    }

    /// <summary>Registers an asynchronous task handler that returns an application exit code.</summary>
    /// <param name="handler">The handler to invoke after successful validation.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder SetTaskHandlerWithExitCode(
        Func<CommandInvocationContext, CancellationToken, Task<int>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore(async (context, cancellationToken) =>
        {
            Task<int> task = handler(context, cancellationToken)
                ?? throw new InvalidOperationException("The asynchronous handler returned a null task.");
            return await task.ConfigureAwait(false);
        });
        return this;
    }

    /// <summary>Registers an asynchronous value-task handler with no application exit code.</summary>
    /// <param name="handler">The handler to invoke after successful validation.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder SetValueTaskHandler(
        Func<CommandInvocationContext, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore(async (context, cancellationToken) =>
        {
            await handler(context, cancellationToken).ConfigureAwait(false);
            return 0;
        });
        return this;
    }

    /// <summary>Registers an asynchronous value-task handler that returns an application exit code.</summary>
    /// <param name="handler">The handler to invoke after successful validation.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder SetValueTaskHandlerWithExitCode(
        Func<CommandInvocationContext, CancellationToken, ValueTask<int>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore((context, cancellationToken) => handler(context, cancellationToken));
        return this;
    }

    private void SetHandlerCore(CommandHandler handler)
    {
        if (Handler is not null)
        {
            throw new InvalidOperationException("A command can have only one handler.");
        }

        Handler = handler;
    }

    internal int ChildCount => _children.Count;

    internal bool HasConverters { get; private set; }

    internal ConverterRegistry CopyConverters() => _converters.Build();

    internal int DirectiveCount => _directives.Count;

    internal int OptionCount => _options.Count;

    internal int PackCount => _packs.Count;

    internal int PositionalCount => _positionals.Count;

    internal CommandHandler? Handler { get; private set; }

    internal DirectiveBuilder[] CopyDirectives() => [.. _directives];

    internal string[] CopyAliases() => [.. _aliases];

    internal string[] CopyExamples() => [.. _examples];

    internal CommandBuilder[] CopyChildren() => [.. _children];

    internal OptionDefinition[] CopyOptions() => [.. _options];

    internal OptionPackInclusionBuilder[] CopyPacks() => [.. _packs];

    internal PositionalDefinition[] CopyPositionals() => [.. _positionals];

    internal CrossValueValidator[] CopyCrossValidators() => [.. _crossValidators];
}
