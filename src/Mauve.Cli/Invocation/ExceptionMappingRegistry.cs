using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Mauve.Cli.Invocation;

/// <summary>Builds immutable snapshots of explicitly typed exception mappings.</summary>
public sealed class ExceptionMappingRegistryBuilder
{
    private readonly Dictionary<Type, ExceptionMapping> _mappings = [];

    /// <summary>Maps an exception class and its subclasses to a constant safe message.</summary>
    /// <typeparam name="TException">The exception class to match.</typeparam>
    /// <param name="exitCode">The nonzero application exit code.</param>
    /// <param name="safeMessage">The explicitly safe public message.</param>
    /// <returns>This builder.</returns>
    public ExceptionMappingRegistryBuilder Map<TException>(int exitCode, string safeMessage)
        where TException : Exception
    {
        CliExceptionResult result = new(exitCode, safeMessage);
        return AddMapping<TException>(_ => result);
    }

    /// <summary>Maps an exception class and its subclasses using a typed safe-message factory.</summary>
    /// <typeparam name="TException">The exception class to match.</typeparam>
    /// <param name="exitCode">The nonzero application exit code.</param>
    /// <param name="safeMessageFactory">A thread-safe callback that excludes sensitive exception context from its result.</param>
    /// <returns>This builder.</returns>
    public ExceptionMappingRegistryBuilder Map<TException>(int exitCode, Func<TException, string> safeMessageFactory)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(safeMessageFactory);
        return exitCode == 0
            ? throw new ArgumentOutOfRangeException(nameof(exitCode), "An exception cannot report a successful exit code.")
            : AddMapping<TException>(exception => new CliExceptionResult(exitCode, safeMessageFactory(exception)));
    }

    /// <summary>Adds an immutable registry, rejecting duplicates before changing this builder.</summary>
    /// <param name="registry">The mappings to include.</param>
    /// <returns>This builder.</returns>
    public ExceptionMappingRegistryBuilder Add(ExceptionMappingRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        foreach (ExceptionMapping mapping in registry.Mappings)
        {
            if (_mappings.ContainsKey(mapping.ExceptionType))
            {
                throw new InvalidOperationException("An exception mapping for this exact type is already registered.");
            }
        }

        foreach (ExceptionMapping mapping in registry.Mappings)
        {
            _mappings.Add(mapping.ExceptionType, mapping);
        }

        return this;
    }

    /// <summary>Creates an immutable snapshot; later registrations do not modify it.</summary>
    /// <returns>The exception mapping registry.</returns>
    public ExceptionMappingRegistry Build() => new([.. _mappings.Values]);

    private ExceptionMappingRegistryBuilder AddMapping<TException>(Func<TException, CliExceptionResult> map)
        where TException : Exception
    {
        ExceptionMapping mapping = new(typeof(TException), static exception => exception is TException,
            exception => map((TException)exception));
        return _mappings.TryAdd(typeof(TException), mapping)
            ? this
            : throw new InvalidOperationException("An exception mapping for this exact type is already registered.");
    }
}

/// <summary>Contains immutable, host-local exception mappings with most-specific class matching.</summary>
/// <remarks>Matching does not scan assemblies or activate types. Callback state and thread safety remain caller-owned.</remarks>
public sealed class ExceptionMappingRegistry
{
    internal ExceptionMappingRegistry(ExceptionMapping[] mappings)
    {
        Mappings = mappings;
    }

    internal IReadOnlyList<ExceptionMapping> Mappings { get; }

    /// <summary>Creates an empty exception mapping registry.</summary>
    /// <returns>An empty registry.</returns>
    public static ExceptionMappingRegistry Empty() => new([]);

    /// <summary>Resolves an exception using the most-specific assignable registered type.</summary>
    /// <param name="exception">The exception to resolve.</param>
    /// <param name="result">The mapped result when a mapping matches; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a registered mapping matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    /// <remarks>Mapping callback exceptions propagate unchanged. This method does not apply the host's <see cref="CliExitCodeException"/> fallback.</remarks>
    public bool TryResolve(Exception exception, [NotNullWhen(true)] out CliExceptionResult? result)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ExceptionMapping? selected = null;
        foreach (ExceptionMapping mapping in Mappings)
        {
            if (mapping.Matches(exception)
                && (selected is null || selected.ExceptionType.IsAssignableFrom(mapping.ExceptionType)))
            {
                selected = mapping;
            }
        }

        if (selected is null)
        {
            result = null;
            return false;
        }

        result = selected.Map(exception);
        return true;
    }
}

internal sealed record ExceptionMapping(
    Type ExceptionType,
    Func<Exception, bool> Matches,
    Func<Exception, CliExceptionResult> Map);
