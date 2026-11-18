using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mauve.Cli.Conversion;

/// <summary>Builds an immutable registry of explicitly declared converters.</summary>
public sealed class ConverterRegistryBuilder
{
    private readonly Dictionary<Type, IRegisteredConverter> _converters = [];

    /// <summary>Registers one converter for an exact target type.</summary>
    /// <typeparam name="T">The converter target type.</typeparam>
    /// <param name="converter">The converter callback.</param>
    /// <returns>This builder.</returns>
    public ConverterRegistryBuilder Register<T>(ValueConverter<T> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        return !_converters.TryAdd(typeof(T), new RegisteredConverter<T>(converter))
            ? throw new InvalidOperationException(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"A converter for '{typeof(T)}' is already registered."))
            : this;
    }

    /// <summary>Registers an explicit ordinal name map for an enum type.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="values">The exact accepted names and values.</param>
    /// <returns>This builder.</returns>
    public ConverterRegistryBuilder RegisterEnum<TEnum>(IEnumerable<KeyValuePair<string, TEnum>> values)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<string, TEnum> map = [with(StringComparer.Ordinal)];
        foreach (KeyValuePair<string, TEnum> entry in values)
        {
            if (string.IsNullOrEmpty(entry.Key) || !map.TryAdd(entry.Key, entry.Value))
            {
                throw new ArgumentException("Enum names must be nonempty and unique under ordinal comparison.", nameof(values));
            }
        }

        return map.Count == 0
            ? throw new ArgumentException("An enum map must contain at least one name.", nameof(values))
            : Register((value, _) => map.TryGetValue(value, out TEnum result)
            ? ValueConversion.FromValue(result)
            : ValueConversion.Failure<TEnum>("enum.unknown"));
    }

    /// <summary>Creates an immutable registry snapshot.</summary>
    /// <returns>A converter registry.</returns>
    public ConverterRegistry Build()
    {
        return new ConverterRegistry([with(_converters)]);
    }
}

/// <summary>An immutable registry of explicitly declared value converters.</summary>
public sealed class ConverterRegistry
{
    private readonly ReadOnlyDictionary<Type, IRegisteredConverter> _converters;

    internal ConverterRegistry(Dictionary<Type, IRegisteredConverter> converters)
    {
        _converters = new ReadOnlyDictionary<Type, IRegisteredConverter>(converters);
    }

    /// <summary>Creates an empty converter registry.</summary>
    /// <returns>An empty registry.</returns>
    public static ConverterRegistry Empty()
    {
        return new ConverterRegistry([]);
    }

    internal bool TryConvert(
        Type registeredType,
        string value,
        ConversionContext context,
        out object? converted,
        out string? failureCode)
    {
        if (_converters.TryGetValue(registeredType, out IRegisteredConverter? converter))
        {
            RegisteredOutcome result = converter.Convert(value, context);
            converted = result.Value;
            failureCode = result.FailureCode;
            return result.Succeeded;
        }

        converted = null;
        failureCode = null;
        return false;
    }

    internal bool Contains(Type targetType)
    {
        if (_converters.ContainsKey(targetType))
        {
            return true;
        }

        Type? underlying = Nullable.GetUnderlyingType(targetType);
        return underlying is not null && _converters.ContainsKey(underlying);
    }

    internal bool ContainsExact(Type targetType)
    {
        return _converters.ContainsKey(targetType);
    }
}

internal interface IRegisteredConverter
{
    RegisteredOutcome Convert(string value, ConversionContext context);
}

internal sealed class RegisteredConverter<T>(ValueConverter<T> converter) : IRegisteredConverter
{
    public RegisteredOutcome Convert(string value, ConversionContext context)
    {
        ValueConversion<T> result = converter(value, context)
            ?? throw new InvalidOperationException("A converter returned no result.");
        return result.Succeeded && result.FailureCode is not null
            ? throw new InvalidOperationException("A converter returned an invalid success result.")
            : !result.Succeeded && !ConversionFailureCode.IsValid(result.FailureCode)
                ? throw new InvalidOperationException("A converter returned an invalid failure result.")
                : new RegisteredOutcome(result.Succeeded, result.Value, result.FailureCode);
    }
}

internal readonly record struct RegisteredOutcome(bool Succeeded, object? Value, string? FailureCode);

internal static class ConversionFailureCode
{
    public static bool IsValid(string? value)
    {
        if (value is null || value.Length is 0 or > 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (character is not ((>= 'A' and <= 'Z')
                or (>= 'a' and <= 'z')
                or (>= '0' and <= '9')
                or '.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
