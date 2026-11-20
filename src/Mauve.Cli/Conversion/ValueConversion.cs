using System;

namespace Mauve.Cli.Conversion;

/// <summary>The result of a value conversion.</summary>
/// <typeparam name="T">The converted value type.</typeparam>
public sealed class ValueConversion<T>
{
    internal ValueConversion(bool succeeded, T? value, string? failureCode)
    {
        Succeeded = succeeded;
        Value = value;
        FailureCode = failureCode;
    }

    /// <summary>Gets whether conversion succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the converted value when successful.</summary>
    public T? Value { get; }

    /// <summary>Gets the safe reason code when conversion failed.</summary>
    public string? FailureCode { get; }

}

/// <summary>Creates generic conversion outcomes.</summary>
public static class ValueConversion
{
    /// <summary>Creates a successful conversion result.</summary>
    /// <typeparam name="T">The converted value type.</typeparam>
    /// <param name="value">The converted value.</param>
    /// <returns>A successful result.</returns>
    public static ValueConversion<T> FromValue<T>(T? value) => new(true, value, null);

    /// <summary>Creates a failed conversion result.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="failureCode">A bounded ASCII reason identifier.</param>
    /// <returns>A failed result.</returns>
    public static ValueConversion<T> Failure<T>(string failureCode)
    {
        ArgumentNullException.ThrowIfNull(failureCode);
        return new ValueConversion<T>(false, default, failureCode);
    }
}

/// <summary>Converts one complete token to a declared type.</summary>
/// <typeparam name="T">The converted value type.</typeparam>
/// <param name="value">The raw token.</param>
/// <param name="context">The conversion context.</param>
/// <returns>The conversion outcome.</returns>
public delegate ValueConversion<T> ValueConverter<T>(string value, ConversionContext context);
