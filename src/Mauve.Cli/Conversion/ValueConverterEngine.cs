using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Mauve.Cli.Conversion;

internal static class ValueConverterEngine
{
    private const NumberStyles _integerStyles = NumberStyles.AllowLeadingSign;
    private const NumberStyles _floatingStyles = NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowExponent;
    private const NumberStyles _decimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static CultureInfo CreateNumericCulture(CultureInfo? culture)
    {
        string separator = culture?.NumberFormat.NumberDecimalSeparator ?? CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator;
        if (separator.Length != 1
            || char.IsAsciiDigit(separator[0])
            || separator[0] is '+' or '-' or 'e' or 'E'
            || char.IsWhiteSpace(separator[0]))
        {
            throw new ArgumentException("The numeric culture must use a supported single-character decimal separator.", nameof(culture));
        }

        NumberFormatInfo numberFormat = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        numberFormat.NumberDecimalSeparator = separator;
        CultureInfo numericCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        numericCulture.NumberFormat = numberFormat;
        return CultureInfo.ReadOnly(numericCulture);
    }

    public static bool IsSupported(Type type, ConverterRegistry registry)
    {
        Type? nullableType = Nullable.GetUnderlyingType(type);
        Type effectiveType = nullableType ?? type;
        return registry.Contains(type) || IsBuiltIn(effectiveType);
    }

    public static bool TryConvert(
        string raw,
        Type targetType,
        ConversionContext context,
        ConverterRegistry registry,
        out object? value,
        out string failureCode)
    {
        Type? nullableType = Nullable.GetUnderlyingType(targetType);
        Type builtInType = nullableType ?? targetType;
        Type registeredType = registry.ContainsExact(targetType) ? targetType : builtInType;
        return registry.ContainsExact(registeredType)
            ? TryRegistered(raw, registeredType, context, registry, out value, out failureCode)
            : TryBuiltIn(raw, builtInType, context.NumericCulture, out value, out failureCode);
    }

    public static bool IsNumericType(Type type)
    {
        Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        return effectiveType == typeof(sbyte)
            || effectiveType == typeof(byte)
            || effectiveType == typeof(short)
            || effectiveType == typeof(ushort)
            || effectiveType == typeof(int)
            || effectiveType == typeof(uint)
            || effectiveType == typeof(long)
            || effectiveType == typeof(ulong)
            || effectiveType == typeof(float)
            || effectiveType == typeof(double)
            || effectiveType == typeof(decimal);
    }

    public static object CreateReadOnlyCollection(
        Type elementType,
        IReadOnlyList<object?> values,
        ConverterRegistry registry)
    {
        return registry.TryCreateReadOnlyCollection(elementType, values, out object? registeredCollection)
            ? registeredCollection!
            : CreateBuiltInReadOnlyCollection(elementType, values);
    }

    private static object CreateBuiltInReadOnlyCollection(Type elementType, IReadOnlyList<object?> values)
    {
        return elementType == typeof(string) ? CreateTypedReadOnlyCollection<string>(values)
            : elementType == typeof(bool) ? CreateTypedReadOnlyCollection<bool>(values)
            : elementType == typeof(bool?) ? CreateTypedReadOnlyCollection<bool?>(values)
            : elementType == typeof(sbyte) ? CreateTypedReadOnlyCollection<sbyte>(values)
            : elementType == typeof(sbyte?) ? CreateTypedReadOnlyCollection<sbyte?>(values)
            : elementType == typeof(byte) ? CreateTypedReadOnlyCollection<byte>(values)
            : elementType == typeof(byte?) ? CreateTypedReadOnlyCollection<byte?>(values)
            : elementType == typeof(short) ? CreateTypedReadOnlyCollection<short>(values)
            : elementType == typeof(short?) ? CreateTypedReadOnlyCollection<short?>(values)
            : elementType == typeof(ushort) ? CreateTypedReadOnlyCollection<ushort>(values)
            : elementType == typeof(ushort?) ? CreateTypedReadOnlyCollection<ushort?>(values)
            : elementType == typeof(int) ? CreateTypedReadOnlyCollection<int>(values)
            : elementType == typeof(int?) ? CreateTypedReadOnlyCollection<int?>(values)
            : elementType == typeof(uint) ? CreateTypedReadOnlyCollection<uint>(values)
            : elementType == typeof(uint?) ? CreateTypedReadOnlyCollection<uint?>(values)
            : elementType == typeof(long) ? CreateTypedReadOnlyCollection<long>(values)
            : elementType == typeof(long?) ? CreateTypedReadOnlyCollection<long?>(values)
            : elementType == typeof(ulong) ? CreateTypedReadOnlyCollection<ulong>(values)
            : elementType == typeof(ulong?) ? CreateTypedReadOnlyCollection<ulong?>(values)
            : elementType == typeof(float) ? CreateTypedReadOnlyCollection<float>(values)
            : elementType == typeof(float?) ? CreateTypedReadOnlyCollection<float?>(values)
            : elementType == typeof(double) ? CreateTypedReadOnlyCollection<double>(values)
            : elementType == typeof(double?) ? CreateTypedReadOnlyCollection<double?>(values)
            : elementType == typeof(decimal) ? CreateTypedReadOnlyCollection<decimal>(values)
            : elementType == typeof(decimal?) ? CreateTypedReadOnlyCollection<decimal?>(values)
            : elementType == typeof(Guid) ? CreateTypedReadOnlyCollection<Guid>(values)
            : elementType == typeof(Guid?) ? CreateTypedReadOnlyCollection<Guid?>(values)
            : elementType == typeof(Uri) ? CreateTypedReadOnlyCollection<Uri>(values)
            : throw new InvalidOperationException("The model contains an unsupported collection element type.");
    }

    private static bool TryRegistered(
        string raw,
        Type registeredType,
        ConversionContext context,
        ConverterRegistry registry,
        out object? value,
        out string failureCode)
    {
        bool succeeded = registry.TryConvert(registeredType, raw, context, out value, out string? registeredFailure);
        failureCode = registeredFailure ?? "conversion.invalid";
        return succeeded;
    }

    private static bool TryBuiltIn(
        string raw,
        Type targetType,
        CultureInfo culture,
        out object? value,
        out string failureCode)
    {
        value = null;
        failureCode = "conversion.invalid";

        if (targetType == typeof(string))
        {
            value = raw;
            return true;
        }

        if (targetType == typeof(bool))
        {
            if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            return false;
        }

        if (targetType == typeof(sbyte) && TryInteger(raw, sbyte.TryParse, out sbyte signedByte))
        {
            value = signedByte;
            return true;
        }

        if (targetType == typeof(byte) && TryInteger(raw, byte.TryParse, out byte unsignedByte))
        {
            value = unsignedByte;
            return true;
        }

        if (targetType == typeof(short) && TryInteger(raw, short.TryParse, out short signedShort))
        {
            value = signedShort;
            return true;
        }

        if (targetType == typeof(ushort) && TryInteger(raw, ushort.TryParse, out ushort unsignedShort))
        {
            value = unsignedShort;
            return true;
        }

        if (targetType == typeof(int) && TryInteger(raw, int.TryParse, out int signedInteger))
        {
            value = signedInteger;
            return true;
        }

        if (targetType == typeof(uint) && TryInteger(raw, uint.TryParse, out uint unsignedInteger))
        {
            value = unsignedInteger;
            return true;
        }

        if (targetType == typeof(long) && TryInteger(raw, long.TryParse, out long signedLong))
        {
            value = signedLong;
            return true;
        }

        if (targetType == typeof(ulong) && TryInteger(raw, ulong.TryParse, out ulong unsignedLong))
        {
            value = unsignedLong;
            return true;
        }

        if (targetType == typeof(float)
            && IsNumericSyntax(raw, culture.NumberFormat.NumberDecimalSeparator, allowDecimal: true, allowExponent: true)
            && float.TryParse(raw, _floatingStyles, culture, out float single)
            && float.IsFinite(single))
        {
            value = single;
            return true;
        }

        if (targetType == typeof(double)
            && IsNumericSyntax(raw, culture.NumberFormat.NumberDecimalSeparator, allowDecimal: true, allowExponent: true)
            && double.TryParse(raw, _floatingStyles, culture, out double doublePrecision)
            && double.IsFinite(doublePrecision))
        {
            value = doublePrecision;
            return true;
        }

        if (targetType == typeof(decimal)
            && IsNumericSyntax(raw, culture.NumberFormat.NumberDecimalSeparator, allowDecimal: true, allowExponent: false)
            && decimal.TryParse(raw, _decimalStyles, culture, out decimal decimalValue))
        {
            value = decimalValue;
            return true;
        }

        if (targetType == typeof(Guid) && Guid.TryParseExact(raw, "D", out Guid guid))
        {
            value = guid;
            return true;
        }

        if (targetType == typeof(Uri) && TryAbsoluteUri(raw, out Uri? uri))
        {
            value = uri;
            return true;
        }

        failureCode = IsBuiltIn(targetType) ? "conversion.invalid" : "conversion.unsupported";
        return false;
    }

    private static bool TryInteger<T>(
        string raw,
        TryParse<T> parser,
        out T result)
    {
        if (!IsIntegerSyntax(raw) || !parser(raw, _integerStyles, CultureInfo.InvariantCulture, out result))
        {
            result = default!;
            return false;
        }

        return true;
    }

    private static bool IsIntegerSyntax(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        int index = value[0] is '+' or '-' ? 1 : 0;
        if (index == value.Length)
        {
            return false;
        }

        for (; index < value.Length; index++)
        {
            if (!char.IsAsciiDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumericSyntax(
        string value,
        string decimalSeparator,
        bool allowDecimal,
        bool allowExponent)
    {
        if (value.Length == 0)
        {
            return false;
        }

        int index = value[0] is '+' or '-' ? 1 : 0;
        int mantissaDigits = 0;
        bool foundSeparator = false;
        while (index < value.Length)
        {
            if (char.IsAsciiDigit(value[index]))
            {
                mantissaDigits++;
                index++;
                continue;
            }

            if (allowDecimal
                && !foundSeparator
                && value.AsSpan(index).StartsWith(decimalSeparator.AsSpan(), StringComparison.Ordinal))
            {
                foundSeparator = true;
                index += decimalSeparator.Length;
                continue;
            }

            break;
        }

        if (mantissaDigits == 0)
        {
            return false;
        }

        if (index < value.Length && allowExponent && value[index] is 'e' or 'E')
        {
            index++;
            if (index < value.Length && value[index] is '+' or '-')
            {
                index++;
            }

            int exponentDigits = 0;
            while (index < value.Length && char.IsAsciiDigit(value[index]))
            {
                exponentDigits++;
                index++;
            }

            return exponentDigits > 0 && index == value.Length;
        }

        return index == value.Length;
    }

    private static bool TryAbsoluteUri(string value, out Uri? uri)
    {
        uri = null;
        return HasScheme(value)
            && !value.StartsWith('/', StringComparison.Ordinal)
            && !value.StartsWith('\\', StringComparison.Ordinal)
            && !(value.Length > 2 && value[1] == ':' && (value[2] is '/' or '\\'))
            && Uri.TryCreate(value, UriKind.Absolute, out uri)
            && Uri.IsWellFormedUriString(value, UriKind.Absolute);
    }

    private static bool HasScheme(string value)
    {
        int separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || !IsAsciiLetter(value[0]))
        {
            return false;
        }

        for (int index = 1; index < separator; index++)
        {
            char character = value[index];
            if (!(IsAsciiLetter(character) || char.IsAsciiDigit(character) || character is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBuiltIn(Type type)
    {
        return type == typeof(string)
            || type == typeof(bool)
            || type == typeof(sbyte)
            || type == typeof(byte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(Uri);
    }

    private static ReadOnlyCollection<T> CreateTypedReadOnlyCollection<T>(IReadOnlyList<object?> values)
    {
        List<T> typedValues = [];
        foreach (object? value in values)
        {
            typedValues.Add(value is T typedValue
                ? typedValue
                : value is null && default(T) is null
                    ? default!
                    : throw new InvalidOperationException("A converted collection element has the wrong type."));
        }

        return typedValues.AsReadOnly();
    }

    private static bool IsAsciiLetter(char character)
    {
        return character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
    }

    private delegate bool TryParse<T>(
        string value,
        NumberStyles styles,
        IFormatProvider provider,
        out T result);
}
