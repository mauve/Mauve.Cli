using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Mauve.Cli.Conversion;

/// <summary>Registers ordered, statically referenced case factories for a union.</summary>
/// <typeparam name="TUnion">The union target type.</typeparam>
public sealed class UnionConverterBuilder<TUnion>
    where TUnion : IUnion
{
    private readonly List<IUnionCase<TUnion>> _cases = [];
    private readonly HashSet<Type> _types = [];

    /// <summary>Adds a case; the first case whose conversion succeeds wins.</summary>
    /// <typeparam name="TCase">The case value type.</typeparam>
    /// <param name="factory">The statically referenced union constructor.</param>
    /// <returns>This builder.</returns>
    public UnionConverterBuilder<TUnion> Case<TCase>(Func<TCase, TUnion> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!_types.Add(typeof(TCase)))
        {
            throw new ArgumentException("Union case types must be unique.", nameof(factory));
        }

        _cases.Add(new UnionCase<TUnion, TCase>(factory));
        return this;
    }

    internal IRegisteredConverter Build()
    {
        return _cases.Count == 0
            ? throw new InvalidOperationException("A union converter requires at least one case.")
            : new RegisteredUnionConverter<TUnion>([.. _cases]);
    }
}

internal interface IUnionCase<TUnion>
{
    Type CaseType { get; }

    RegisteredOutcome Convert(string value, ConversionContext context, ConverterRegistry registry);
}

internal sealed class UnionCase<TUnion, TCase>(Func<TCase, TUnion> factory) : IUnionCase<TUnion>
{
    public Type CaseType => typeof(TCase);

    public RegisteredOutcome Convert(string value, ConversionContext context, ConverterRegistry registry)
    {
        ConversionContext caseContext = new(
            typeof(TCase), context.DefinitionName, context.TokenIndex, context.NumericCulture, context.IsSensitive);
        return !ValueConverterEngine.TryConvert(value, typeof(TCase), caseContext, registry, out object? converted, out string failure)
            ? new RegisteredOutcome(false, null, failure)
            : converted is TCase typed
            ? new RegisteredOutcome(true, factory(typed), null)
            : converted is null && default(TCase) is null
                ? new RegisteredOutcome(true, factory(default!), null)
                : throw new InvalidOperationException("A union case converter returned a value of the wrong type.");
    }
}

internal sealed class RegisteredUnionConverter<TUnion>(IUnionCase<TUnion>[] cases) : IRegisteredConverter
{
    public IReadOnlyList<Type> Dependencies
    {
        get
        {
            List<Type> types = [];
            foreach (IUnionCase<TUnion> unionCase in cases)
            {
                types.Add(unionCase.CaseType);
            }

            return types;
        }
    }

    public RegisteredOutcome Convert(string value, ConversionContext context, ConverterRegistry registry)
    {
        foreach (IUnionCase<TUnion> unionCase in cases)
        {
            RegisteredOutcome result = unionCase.Convert(value, context, registry);
            if (result.Succeeded)
            {
                return result;
            }
        }

        return new RegisteredOutcome(false, null, "union.no-match");
    }

    public object CreateReadOnlyCollection(IReadOnlyList<object?> values, bool useNullableElement)
    {
        return new RegisteredConverter<TUnion>(static (_, _) =>
            throw new InvalidOperationException("A collection factory cannot convert input."))
            .CreateReadOnlyCollection(values, useNullableElement);
    }
}
