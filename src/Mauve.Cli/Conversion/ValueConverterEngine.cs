using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security;

namespace Mauve.Cli.Conversion;

internal static class ValueConverterEngine
{
    private const NumberStyles _integerStyles = NumberStyles.AllowLeadingSign;
    private const NumberStyles _floatingStyles = NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint
        | NumberStyles.AllowExponent;
    private const NumberStyles _decimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
    private static readonly SearchValues<char> _addressInvalidCharacters = SearchValues.Create(" \t\r\n[]");

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
            || effectiveType == typeof(Half)
            || effectiveType == typeof(float)
            || effectiveType == typeof(double)
            || effectiveType == typeof(decimal);
    }

    public static object CreateReadOnlyCollection(
        Type elementType,
        IReadOnlyList<object?> values,
        ConverterRegistry registry)
    {
        return Nullable.GetUnderlyingType(elementType) is Type underlying
            && !registry.ContainsExact(elementType)
            && IsBuiltIn(underlying)
            ? CreateBuiltInReadOnlyCollection(elementType, values)
            : registry.TryCreateReadOnlyCollection(elementType, values, out object? registeredCollection)
            ? registeredCollection
            : CreateBuiltInReadOnlyCollection(elementType, values);
    }

    public static bool CanCreateCollection(Type elementType, ConverterRegistry registry)
        => registry.ContainsExact(elementType) || IsBuiltIn(Nullable.GetUnderlyingType(elementType) ?? elementType);

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
            : elementType == typeof(Half) ? CreateTypedReadOnlyCollection<Half>(values)
            : elementType == typeof(Half?) ? CreateTypedReadOnlyCollection<Half?>(values)
            : elementType == typeof(float) ? CreateTypedReadOnlyCollection<float>(values)
            : elementType == typeof(float?) ? CreateTypedReadOnlyCollection<float?>(values)
            : elementType == typeof(double) ? CreateTypedReadOnlyCollection<double>(values)
            : elementType == typeof(double?) ? CreateTypedReadOnlyCollection<double?>(values)
            : elementType == typeof(decimal) ? CreateTypedReadOnlyCollection<decimal>(values)
            : elementType == typeof(decimal?) ? CreateTypedReadOnlyCollection<decimal?>(values)
            : elementType == typeof(Guid) ? CreateTypedReadOnlyCollection<Guid>(values)
            : elementType == typeof(Guid?) ? CreateTypedReadOnlyCollection<Guid?>(values)
            : elementType == typeof(Uri) ? CreateTypedReadOnlyCollection<Uri>(values)
            : elementType == typeof(char) ? CreateTypedReadOnlyCollection<char>(values)
            : elementType == typeof(char?) ? CreateTypedReadOnlyCollection<char?>(values)
            : elementType == typeof(DateOnly) ? CreateTypedReadOnlyCollection<DateOnly>(values)
            : elementType == typeof(DateOnly?) ? CreateTypedReadOnlyCollection<DateOnly?>(values)
            : elementType == typeof(TimeOnly) ? CreateTypedReadOnlyCollection<TimeOnly>(values)
            : elementType == typeof(TimeOnly?) ? CreateTypedReadOnlyCollection<TimeOnly?>(values)
            : elementType == typeof(DateTimeOffset) ? CreateTypedReadOnlyCollection<DateTimeOffset>(values)
            : elementType == typeof(DateTimeOffset?) ? CreateTypedReadOnlyCollection<DateTimeOffset?>(values)
            : elementType == typeof(TimeSpan) ? CreateTypedReadOnlyCollection<TimeSpan>(values)
            : elementType == typeof(TimeSpan?) ? CreateTypedReadOnlyCollection<TimeSpan?>(values)
            : elementType == typeof(Version) ? CreateTypedReadOnlyCollection<Version>(values)
            : elementType == typeof(IPAddress) ? CreateTypedReadOnlyCollection<IPAddress>(values)
            : elementType == typeof(FileInfo) ? CreateTypedReadOnlyCollection<FileInfo>(values)
            : elementType == typeof(DirectoryInfo) ? CreateTypedReadOnlyCollection<DirectoryInfo>(values)
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
        [NotNullWhen(true)] out object? value,
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

        if (targetType == typeof(Half)
            && IsNumericSyntax(raw, culture.NumberFormat.NumberDecimalSeparator, allowDecimal: true, allowExponent: true)
            && Half.TryParse(raw, _floatingStyles, culture, out Half half)
            && Half.IsFinite(half))
        {
            value = half;
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

        if (targetType == typeof(char) && raw.Length == 1 && !char.IsSurrogate(raw[0]))
        {
            value = raw[0];
            return true;
        }

        if (targetType == typeof(DateOnly)
            && DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            value = date;
            return true;
        }

        if (targetType == typeof(TimeOnly) && TryTime(raw.AsSpan(), out string? timeFormat)
            && TimeOnly.TryParseExact(raw, timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
        {
            value = time;
            return true;
        }

        if (targetType == typeof(DateTimeOffset) && TryDateTimeOffset(raw, out DateTimeOffset dateTimeOffset))
        {
            value = dateTimeOffset;
            return true;
        }

        if (targetType == typeof(TimeSpan) && !raw.AsSpan().ContainsAny(" \t\r\n")
            && TimeSpan.TryParseExact(raw, "c", CultureInfo.InvariantCulture, out TimeSpan duration))
        {
            value = duration;
            return true;
        }

        if (targetType == typeof(Version) && IsVersionSyntax(raw) && Version.TryParse(raw, out Version? version))
        {
            value = version;
            return true;
        }

        if (targetType == typeof(IPAddress) && TryAddress(raw, out IPAddress? address))
        {
            value = address;
            return true;
        }

        if ((targetType == typeof(FileInfo) || targetType == typeof(DirectoryInfo))
            && TryPath(raw, targetType == typeof(FileInfo), out object? path))
        {
            value = path;
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

    private static bool TryAbsoluteUri(string value, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        return HasScheme(value)
            && !value.StartsWith('/', StringComparison.Ordinal)
            && !value.StartsWith('\\', StringComparison.Ordinal)
            && !(value.Length > 2 && value[1] == ':' && (value[2] is '/' or '\\'))
            && Uri.TryCreate(value, UriKind.Absolute, out uri)
            && Uri.IsWellFormedUriString(value, UriKind.Absolute);
    }

    private static bool TryTime(ReadOnlySpan<char> value, [NotNullWhen(true)] out string? format)
    {
        format = null;
        if (value.Length < 5 || value[2] != ':' || !AreAsciiDigits(value[..2]) || !AreAsciiDigits(value.Slice(3, 2))
            || value[..2].CompareTo("23", StringComparison.Ordinal) > 0
            || value.Slice(3, 2).CompareTo("59", StringComparison.Ordinal) > 0)
        {
            return false;
        }

        if (value.Length == 5)
        {
            format = "HH:mm";
            return true;
        }

        if (value.Length < 8 || value[5] != ':' || !AreAsciiDigits(value.Slice(6, 2))
            || value.Slice(6, 2).CompareTo("59", StringComparison.Ordinal) > 0)
        {
            return false;
        }

        if (value.Length == 8)
        {
            format = "HH:mm:ss";
            return true;
        }

        if (value.Length is < 10 or > 16 || value[8] != '.' || !AreAsciiDigits(value[9..]))
        {
            return false;
        }

        format = "HH:mm:ss.FFFFFFF";
        return true;
    }

    private static bool TryDateTimeOffset(string raw, out DateTimeOffset result)
    {
        result = default;
        if (raw.Length < 17 || raw[10] != 'T')
        {
            return false;
        }

        bool utc = raw[^1] == 'Z';
        int offsetStart = utc ? raw.Length - 1 : raw.Length - 6;
        if (offsetStart <= 11 || (!utc && (raw[offsetStart] is not ('+' or '-')
            || raw[offsetStart + 3] != ':'
            || !AreAsciiDigits(raw.AsSpan(offsetStart + 1, 2))
            || !AreAsciiDigits(raw.AsSpan(offsetStart + 4, 2)))))
        {
            return false;
        }

        if (!TryTime(raw.AsSpan(11, offsetStart - 11), out string? timeFormat))
        {
            return false;
        }

        string format = "yyyy-MM-dd'T'" + timeFormat + (utc ? "'Z'" : "zzz");
        DateTimeStyles styles = utc ? DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal : DateTimeStyles.None;
        return DateTimeOffset.TryParseExact(raw, format, CultureInfo.InvariantCulture, styles, out result);
    }

    private static bool AreAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return !value.IsEmpty;
    }

    private static bool IsVersionSyntax(string value)
    {
        int componentCount = 0;
        foreach (Range range in value.AsSpan().Split('.'))
        {
            if (!AreAsciiDigits(value.AsSpan()[range]))
            {
                return false;
            }

            componentCount++;
        }

        return componentCount is >= 2 and <= 4;
    }

    private static bool TryAddress(string raw, [NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        if (raw.AsSpan().ContainsAny(_addressInvalidCharacters))
        {
            return false;
        }

        int colon = raw.LastIndexOf(':', StringComparison.Ordinal);
        return (!raw.Contains('.', StringComparison.Ordinal) || IsIpv4Syntax(raw.AsSpan((colon + 1)..)))
            && (colon >= 0 || IsIpv4Syntax(raw.AsSpan()))
            && IPAddress.TryParse(raw, out address)
            && (colon >= 0 ? address.AddressFamily == AddressFamily.InterNetworkV6 : address.AddressFamily == AddressFamily.InterNetwork);
    }

    private static bool IsIpv4Syntax(ReadOnlySpan<char> value)
    {
        int count = 0;
        foreach (Range range in value.Split('.'))
        {
            ReadOnlySpan<char> component = value[range];
            if (component.Length is < 1 or > 3 || (component.Length > 1 && component[0] == '0')
                || !AreAsciiDigits(component)
                || !byte.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            count++;
        }

        return count == 4;
    }

    private static bool TryPath(string raw, bool file, [NotNullWhen(true)] out object? result)
    {
        result = null;
        if (raw.Length == 0)
        {
            return false;
        }

        try
        {
            result = file ? new FileInfo(raw) : new DirectoryInfo(raw);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return false;
        }
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
            || type == typeof(Half)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(Uri)
            || type == typeof(char)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Version)
            || type == typeof(IPAddress)
            || type == typeof(FileInfo)
            || type == typeof(DirectoryInfo);
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

    private static bool IsAsciiLetter(char character) => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private delegate bool TryParse<T>(
        string value,
        NumberStyles styles,
        IFormatProvider provider,
        out T result);
}
