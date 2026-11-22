using System;
using System.Collections.Generic;

namespace Mauve.Cli.Conversion;

/// <summary>An immutable, typed converter for one scalar or collection element definition.</summary>
public sealed class ValueConverterRegistration
{
    private readonly IRegisteredConverter _converter;

    private ValueConverterRegistration(Type targetType, IRegisteredConverter converter, bool completionSafe)
    {
        TargetType = targetType;
        _converter = converter;
        IsCompletionSafe = completionSafe;
    }

    /// <summary>Gets the exact scalar or element target type.</summary>
    public Type TargetType { get; }

    /// <summary>Gets whether completion may execute this converter.</summary>
    public bool IsCompletionSafe { get; }

    /// <summary>Creates a definition-local converter registration.</summary>
    /// <typeparam name="T">The exact target type.</typeparam>
    /// <param name="converter">The application callback.</param>
    /// <param name="completionSafe">Whether the callback is safe for partial completion context.</param>
    /// <returns>An immutable registration.</returns>
    public static ValueConverterRegistration Create<T>(ValueConverter<T> converter, bool completionSafe = false)
    {
        ArgumentNullException.ThrowIfNull(converter);
        return new ValueConverterRegistration(typeof(T), new RegisteredConverter<T>(converter), completionSafe);
    }

    /// <summary>Creates a registration referencing a static converter provider.</summary>
    /// <typeparam name="T">The exact target type.</typeparam>
    /// <typeparam name="TConverter">The statically referenced provider.</typeparam>
    /// <param name="completionSafe">Whether completion may execute this provider.</param>
    /// <returns>An immutable registration.</returns>
    public static ValueConverterRegistration Create<T, TConverter>(bool completionSafe = false)
        where TConverter : ICliValueConverter<T>
        => Create(TConverter.Convert, completionSafe);

    internal RegisteredOutcome Convert(string value, ConversionContext context, ConverterRegistry registry)
        => _converter.Convert(value, context, registry);

    internal object CreateReadOnlyCollection(IReadOnlyList<object?> values)
        => _converter.CreateReadOnlyCollection(values, useNullableElement: false);
}
