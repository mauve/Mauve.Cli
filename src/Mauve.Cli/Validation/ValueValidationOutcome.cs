using System;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;

namespace Mauve.Cli.Validation;

/// <summary>The result of a value or cross-value validation.</summary>
public readonly record struct ValueValidationOutcome
{
    private ValueValidationOutcome(bool succeeded, string? failureCode, object? target, int? itemIndex, int? valueOffset, string? message = null)
    {
        Succeeded = succeeded;
        FailureCode = failureCode;
        Target = target;
        ItemIndex = itemIndex;
        ValueOffset = valueOffset;
        Message = message;
    }

    /// <summary>Gets whether validation succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the safe failure identifier, when validation failed.</summary>
    public string? FailureCode { get; }

    /// <summary>Gets the bounded, single-line user-facing rejection reason, when supplied.</summary>
    /// <remarks>Authors must not include secrets or raw input. Exception messages are never used implicitly.</remarks>
    public string? Message { get; }

    /// <summary>Gets the target option or positional definition, when specified.</summary>
    public object? Target { get; }

    /// <summary>Gets the zero-based collection item index, when specified.</summary>
    public int? ItemIndex { get; }

    /// <summary>Gets the UTF-16 offset relative to the supplying value, when specified.</summary>
    public int? ValueOffset { get; }

    /// <summary>Gets the successful validation outcome.</summary>
    public static ValueValidationOutcome Success { get; } = new(true, null, null, null, null);

    /// <summary>Creates a failed validation outcome.</summary>
    /// <param name="failureCode">A bounded ASCII reason identifier.</param>
    /// <param name="message">A user-facing reason without secrets or raw input.</param>
    /// <returns>A failed outcome.</returns>
    public static ValueValidationOutcome Failure(string failureCode, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(failureCode);
        return !ValidationFailureCode.IsValid(failureCode)
            ? throw new InvalidOperationException("A validator returned an invalid failure code.")
            : new ValueValidationOutcome(false, failureCode, null, null, null, SanitizeMessage(message));
    }

    /// <summary>Creates a failure targeting an option or one of its collection items.</summary>
    public static ValueValidationOutcome Failure(string failureCode, OptionDefinition option, int? itemIndex = null, int? valueOffset = null, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(option);
        return CreateFailure(failureCode, option, itemIndex, valueOffset, message);
    }

    /// <summary>Creates a failure targeting a positional argument or one of its collection items.</summary>
    public static ValueValidationOutcome Failure(string failureCode, PositionalDefinition positional, int? itemIndex = null, int? valueOffset = null, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(positional);
        return CreateFailure(failureCode, positional, itemIndex, valueOffset, message);
    }

    /// <summary>Creates a failure targeting an item of the value currently being validated.</summary>
    public static ValueValidationOutcome FailureForItem(string failureCode, int itemIndex, int? valueOffset = null, string? message = null) =>
        CreateFailure(failureCode, null, itemIndex, valueOffset, message);

    /// <summary>Creates a failure at a UTF-16 offset within the current value.</summary>
    /// <param name="failureCode">A bounded ASCII reason identifier.</param>
    /// <param name="valueOffset">The zero-based offset within the value.</param>
    /// <param name="message">A user-facing reason without secrets or raw input.</param>
    /// <returns>A failed outcome.</returns>
    public static ValueValidationOutcome FailureAt(string failureCode, int valueOffset, string? message = null) =>
        CreateFailure(failureCode, null, null, valueOffset, message);

    /// <summary>Creates a failure targeting a parameter of a generated cross-value validator.</summary>
    /// <param name="failureCode">A bounded ASCII reason identifier.</param>
    /// <param name="parameterIndex">The zero-based validator parameter index.</param>
    /// <param name="itemIndex">An optional zero-based collection item index.</param>
    /// <param name="valueOffset">An optional UTF-16 offset within the value.</param>
    /// <param name="message">A user-facing reason without secrets or raw input.</param>
    /// <returns>A failed outcome.</returns>
    public static ValueValidationOutcome FailureForParameter(string failureCode, int parameterIndex, int? itemIndex = null, int? valueOffset = null, string? message = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(parameterIndex);
        return CreateFailure(failureCode, new ParameterTarget(parameterIndex), itemIndex, valueOffset, message);
    }

    /// <summary>Resolves a generated validator's parameter target to a definition handle.</summary>
    /// <param name="definitions">Definitions in validator parameter order.</param>
    /// <returns>The resolved outcome.</returns>
    public ValueValidationOutcome ResolveParameters(params object[] definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return Target is not ParameterTarget target
            ? this
            : target.Index >= definitions.Length
            || definitions[target.Index] is not (OptionDefinition or PositionalDefinition)
                ? throw new InvalidOperationException("A validator returned an invalid parameter target.")
                : CreateFailure(FailureCode!, definitions[target.Index], ItemIndex, ValueOffset, Message);
    }

    internal ValueValidationOutcome WithItemIndex(int itemIndex) =>
        CreateFailure(FailureCode!, Target, itemIndex, ValueOffset, Message);

    private static ValueValidationOutcome CreateFailure(string failureCode, object? target, int? itemIndex, int? valueOffset, string? message = null)
    {
        _ = Failure(failureCode);
        return itemIndex < 0
            ? throw new ArgumentOutOfRangeException(nameof(itemIndex))
            : valueOffset < 0
                ? throw new ArgumentOutOfRangeException(nameof(valueOffset))
                : new ValueValidationOutcome(false, failureCode, target, itemIndex, valueOffset, SanitizeMessage(message));
    }

    private static string? SanitizeMessage(string? message)
    {
        const int maximumLength = 512;
        if (message is null)
        {
            return null;
        }

        int length = Math.Min(message.Length, maximumLength);
        if (length < message.Length && char.IsHighSurrogate(message[length - 1]))
        {
            length--;
        }

        string text = HelpTextSanitizer.SingleLine(message[..length]).Trim();
        return text.Length == 0 ? null : text;
    }

    internal sealed record ParameterTarget(int Index);
}

internal static class ValidationFailureCode
{
    public static bool IsValid(string value)
    {
        if (value.Length is 0 or > 64)
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
