using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Definitions;

/// <summary>Builds a command graph that can be validated into an immutable model.</summary>
public sealed class CommandBuilder
{
    private readonly List<string> _aliases = [];
    private readonly List<CommandBuilder> _children = [];
    private readonly List<OptionDefinition> _options = [];
    private readonly List<OptionPackInclusionBuilder> _packs = [];
    private readonly List<PositionalDefinition> _positionals = [];
    private readonly List<CrossValueValidator> _crossValidators = [];

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
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the long description.</summary>
    public string LongDescription { get; set; } = string.Empty;

    /// <summary>Gets or sets whether ordinary help and completion hide this command.</summary>
    public bool IsHidden { get; set; }

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
        _packs.Add(new OptionPackInclusionBuilder(key, pack, title ?? string.Empty, description ?? string.Empty));
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

    internal int OptionCount => _options.Count;

    internal int PackCount => _packs.Count;

    internal int PositionalCount => _positionals.Count;

    internal CommandHandler? Handler { get; private set; }

    internal string[] CopyAliases()
    {
        return [.. _aliases];
    }

    internal CommandBuilder[] CopyChildren()
    {
        return [.. _children];
    }

    internal OptionDefinition[] CopyOptions()
    {
        return [.. _options];
    }

    internal OptionPackInclusionBuilder[] CopyPacks()
    {
        return [.. _packs];
    }

    internal PositionalDefinition[] CopyPositionals()
    {
        return [.. _positionals];
    }

    internal CrossValueValidator[] CopyCrossValidators()
    {
        return [.. _crossValidators];
    }
}
