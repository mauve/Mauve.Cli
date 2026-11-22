using System;
using System.Collections.Generic;

namespace Mauve.Cli.Conversion;

internal static class ConversionResolver
{
    public static bool IsNumericType(Type type, ConverterRegistry registry)
    {
        Stack<Type> pending = new();
        HashSet<Type> visited = [];
        pending.Push(type);
        while (pending.Count > 0)
        {
            Type current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (ValueConverterEngine.IsNumericType(current))
            {
                return true;
            }

            Type selected = registry.ContainsExact(current) ? current : Nullable.GetUnderlyingType(current) ?? current;
            foreach (Type dependency in registry.GetDependencies(selected))
            {
                pending.Push(dependency);
            }
        }

        return false;
    }

    public static bool IsSupported(Type type, ConverterRegistry registry, ValueConverterRegistration? converter = null)
        => converter is not null || IsSupported(type, registry, []);

    public static bool CanCreateCollection(Type type, ConverterRegistry registry, ValueConverterRegistration? converter)
        => converter is not null || ValueConverterEngine.CanCreateCollection(type, registry);

    private static bool IsSupported(Type type, ConverterRegistry registry, HashSet<Type> active)
    {
        Type selected = registry.ContainsExact(type) ? type : Nullable.GetUnderlyingType(type) ?? type;
        if (!registry.ContainsExact(selected))
        {
            return ValueConverterEngine.IsSupported(selected, registry);
        }

        if (!active.Add(selected))
        {
            throw new InvalidOperationException("Converter registrations contain a recursive union or nullable dependency.");
        }

        foreach (Type dependency in registry.GetDependencies(selected))
        {
            if (!IsSupported(dependency, registry, active))
            {
                return false;
            }
        }

        _ = active.Remove(selected);
        return true;
    }

    public static bool TryConvert(
        string raw,
        Type type,
        ConversionContext context,
        ConverterRegistry registry,
        ValueConverterRegistration? converter,
        out object? value,
        out string failure)
    {
        if (converter is null)
        {
            return ValueConverterEngine.TryConvert(raw, type, context, registry, out value, out failure);
        }

        RegisteredOutcome result = converter.Convert(raw, context, registry);
        value = result.Value;
        failure = result.FailureCode ?? "conversion.invalid";
        return result.Succeeded;
    }

    public static object CreateReadOnlyCollection(
        Type elementType,
        IReadOnlyList<object?> values,
        ConverterRegistry registry,
        ValueConverterRegistration? converter)
        => converter is null
            ? ValueConverterEngine.CreateReadOnlyCollection(elementType, values, registry)
            : converter.CreateReadOnlyCollection(values);

    public static bool IsCompletionSafe(Type type, ConverterRegistry registry, ValueConverterRegistration? converter = null)
        => converter?.IsCompletionSafe ?? IsCompletionSafe(type, registry, []);

    private static bool IsCompletionSafe(Type type, ConverterRegistry registry, HashSet<Type> active)
    {
        Type selected = registry.ContainsExact(type) ? type : Nullable.GetUnderlyingType(type) ?? type;
        if (!registry.ContainsExact(selected))
        {
            return ValueConverterEngine.IsSupported(selected, registry);
        }

        IReadOnlyList<Type> dependencies = registry.GetDependencies(selected);
        if (dependencies.Count == 0)
        {
            return registry.IsCompletionSafe(selected);
        }

        if (!active.Add(selected))
        {
            throw new InvalidOperationException("Completion converters contain a recursive union dependency.");
        }

        foreach (Type dependency in dependencies)
        {
            if (!IsCompletionSafe(dependency, registry, active))
            {
                return false;
            }
        }

        _ = active.Remove(selected);
        return true;
    }
}
