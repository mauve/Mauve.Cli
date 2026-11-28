using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Invocation;

namespace Mauve.Cli.Definitions;

/// <summary>Builds one directive registration on a command model root.</summary>
public sealed class DirectiveBuilder
{
    private readonly List<DirectiveValueDefinition> _values = [];

    /// <summary>Creates a directive builder.</summary>
    /// <param name="name">The directive name without brackets.</param>
    public DirectiveBuilder(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets the canonical directive name.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the display description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets whether help and completion hide this directive.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Adds an ordered bare value definition.</summary>
    /// <param name="value">The value definition.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder AddValue(DirectiveValueDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _values.Add(value);
        return this;
    }

    /// <summary>Registers a synchronous directive handler.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetHandler(Func<DirectiveInvocationContext, CancellationToken, DirectiveResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore((context, token) => new ValueTask<DirectiveResult>(handler(context, token)));
        return this;
    }

    /// <summary>Registers a synchronous directive whose nonzero result stops processing.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetHandler(Func<DirectiveInvocationContext, CancellationToken, int> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore((context, token) => new ValueTask<DirectiveResult>(
            ToDirectiveResult(handler(context, token))));
        return this;
    }

    /// <summary>Registers a synchronous directive that continues after it returns.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetHandler(Action<DirectiveInvocationContext, CancellationToken> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore((context, token) =>
        {
            handler(context, token);
            return new ValueTask<DirectiveResult>(DirectiveResult.ContinueProcessing);
        });
        return this;
    }

    /// <summary>Registers an asynchronous directive handler.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetTaskHandler(Func<DirectiveInvocationContext, CancellationToken, Task<DirectiveResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore(async (context, token) =>
        {
            Task<DirectiveResult> task = handler(context, token)
                ?? throw new InvalidOperationException("The asynchronous directive returned a null task.");
            return await task.ConfigureAwait(false);
        }, isAsync: true);
        return this;
    }

    /// <summary>Registers an asynchronous directive whose nonzero result stops processing.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetTaskHandler(Func<DirectiveInvocationContext, CancellationToken, Task<int>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore(async (context, token) =>
        {
            Task<int> task = handler(context, token)
                ?? throw new InvalidOperationException("The asynchronous directive returned a null task.");
            return ToDirectiveResult(await task.ConfigureAwait(false));
        }, isAsync: true);
        return this;
    }

    /// <summary>Registers an asynchronous directive that continues after it returns.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetTaskHandler(Func<DirectiveInvocationContext, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore(async (context, token) =>
        {
            Task task = handler(context, token)
                ?? throw new InvalidOperationException("The asynchronous directive returned a null task.");
            await task.ConfigureAwait(false);
            return DirectiveResult.ContinueProcessing;
        }, isAsync: true);
        return this;
    }

    /// <summary>Registers an asynchronous directive that continues after it returns.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetValueTaskHandler(Func<DirectiveInvocationContext, CancellationToken, ValueTask<DirectiveResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore((context, token) => handler(context, token), isAsync: true);
        return this;
    }

    /// <summary>Registers an asynchronous directive whose nonzero result stops processing.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetValueTaskHandler(Func<DirectiveInvocationContext, CancellationToken, ValueTask<int>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore(async (context, token) =>
            ToDirectiveResult(await handler(context, token).ConfigureAwait(false)), isAsync: true);
        return this;
    }

    /// <summary>Registers an asynchronous directive that continues after it returns.</summary>
    /// <param name="handler">The handler to invoke after successful parsing.</param>
    /// <returns>This builder.</returns>
    public DirectiveBuilder SetValueTaskHandler(Func<DirectiveInvocationContext, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SetHandlerCore(async (context, token) =>
        {
            await handler(context, token).ConfigureAwait(false);
            return DirectiveResult.ContinueProcessing;
        }, isAsync: true);
        return this;
    }

    internal DirectiveDefinition Build() => new(Name, Description, IsHidden, [.. _values], Handler, IsCompletion);

    internal bool IsCompletion { get; set; }

    internal int ValueCount => _values.Count;

    internal bool IsAsync { get; private set; }

    private void SetHandlerCore(DirectiveHandler handler, bool isAsync = false)
    {
        if (Handler is not null)
        {
            throw new InvalidOperationException("A directive can have only one handler.");
        }

        Handler = handler;
        IsAsync = isAsync;
    }

    private static DirectiveResult ToDirectiveResult(int exitCode)
    {
        return exitCode == 0
            ? DirectiveResult.ContinueProcessing
            : DirectiveResult.StopProcessing(exitCode);
    }

    internal DirectiveHandler? Handler { get; private set; }
}

internal delegate ValueTask<DirectiveResult> DirectiveHandler(
    DirectiveInvocationContext context,
    CancellationToken cancellationToken);
