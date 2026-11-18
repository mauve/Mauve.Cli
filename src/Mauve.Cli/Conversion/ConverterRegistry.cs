using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mauve.Cli.Conversion;

/// <summary>Builds an immutable registry of explicitly declared converters.</summary>
public sealed class ConverterRegistryBuilder
{
    private readonly Dictionary<Type, IRegisteredConverter> _converters = [];
    private readonly HashSet<Type> _completionSafe = [];
    private readonly Dictionary<Type, string[]> _enumNames = [];

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

    /// <summary>Registers a converter whose callback is safe to invoke while resolving completion context.</summary>
    /// <typeparam name="T">The converter target type.</typeparam>
    /// <param name="converter">The converter callback.</param>
    /// <returns>This builder.</returns>
    public ConverterRegistryBuilder RegisterCompletionSafe<T>(ValueConverter<T> converter)
    {
        Register(converter);
        _ = _completionSafe.Add(typeof(T));
        return this;
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
        List<string> names = [];
        foreach (KeyValuePair<string, TEnum> entry in values)
        {
            if (string.IsNullOrEmpty(entry.Key) || !map.TryAdd(entry.Key, entry.Value))
            {
                throw new ArgumentException("Enum names must be nonempty and unique under ordinal comparison.", nameof(values));
            }

            names.Add(entry.Key);
        }

        if (map.Count == 0)
        {
            throw new ArgumentException("An enum map must contain at least one name.", nameof(values));
        }

        RegisterCompletionSafe<TEnum>((value, _) => map.TryGetValue(value, out TEnum result)
            ? ValueConversion.FromValue(result)
            : ValueConversion.Failure<TEnum>("enum.unknown"));
        _enumNames.Add(typeof(TEnum), [.. names]);
        return this;
    }

    /// <summary>Creates an immutable registry snapshot.</summary>
    /// <returns>A converter registry.</returns>
    public ConverterRegistry Build()
    {
        return new ConverterRegistry(
            [with(_converters)],
            [with(_completionSafe)],
            [with(_enumNames)]);
    }
}

/// <summary>An immutable registry of explicitly declared value converters.</summary>
public sealed class ConverterRegistry
{
    private readonly ReadOnlyDictionary<Type, IRegisteredConverter> _converters;
    private readonly HashSet<Type> _completionSafe;
    private readonly ReadOnlyDictionary<Type, string[]> _enumNames;

    internal ConverterRegistry(
        Dictionary<Type, IRegisteredConverter> converters,
        HashSet<Type> completionSafe,
        Dictionary<Type, string[]> enumNames)
    {
        _converters = new ReadOnlyDictionary<Type, IRegisteredConverter>(converters);
        _completionSafe = completionSafe;
        _enumNames = new ReadOnlyDictionary<Type, string[]>(enumNames);
    }

    /// <summary>Creates an empty converter registry.</summary>
    /// <returns>An empty registry.</returns>
    public static ConverterRegistry Empty()
    {
        return new ConverterRegistry([], [], []);
    }

    internal bool IsCompletionSafe(Type targetType)
    {
        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return _completionSafe.Contains(targetType) || _completionSafe.Contains(effectiveType);
    }

    internal IReadOnlyList<string> GetEnumNames(Type targetType)
    {
        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return _enumNames.TryGetValue(effectiveType, out string[]? names) ? names : [];
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

    internal bool TryCreateReadOnlyCollection(
        Type elementType,
        IReadOnlyList<object?> values,
        out object? collection)
    {
        bool useNullableElement = false;
        Type registeredType = elementType;
        if (!_converters.ContainsKey(registeredType)
            && Nullable.GetUnderlyingType(elementType) is Type underlyingType)
        {
            registeredType = underlyingType;
            useNullableElement = true;
        }

        if (_converters.TryGetValue(registeredType, out IRegisteredConverter? converter))
        {
            collection = converter.CreateReadOnlyCollection(values, useNullableElement);
            return true;
        }

        collection = null;
        return false;
    }
}

internal interface IRegisteredConverter
{
    RegisteredOutcome Convert(string value, ConversionContext context);

    object CreateReadOnlyCollection(IReadOnlyList<object?> values, bool useNullableElement);
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

    public object CreateReadOnlyCollection(IReadOnlyList<object?> values, bool useNullableElement)
    {
        return useNullableElement
            ? (object)CreateNullableReadOnlyCollection(values)
            : CreateTypedReadOnlyCollection(values);
    }

    private static ReadOnlyCollection<T> CreateTypedReadOnlyCollection(IReadOnlyList<object?> values)
    {
        List<T> typedValues = [];
        foreach (object? value in values)
        {
            typedValues.Add(CastValue(value));
        }

        return typedValues.AsReadOnly();
    }

    private static ReadOnlyCollection<T?> CreateNullableReadOnlyCollection(IReadOnlyList<object?> values)
    {
        List<T?> typedValues = [];
        foreach (object? value in values)
        {
            typedValues.Add(value is null ? default : CastValue(value));
        }

        return typedValues.AsReadOnly();
    }

    private static T CastValue(object? value)
    {
        return value is T typedValue
                ? typedValue
                : value is null && default(T) is null
                    ? default!
                : throw new InvalidOperationException("A converted collection element has the wrong type.");
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
