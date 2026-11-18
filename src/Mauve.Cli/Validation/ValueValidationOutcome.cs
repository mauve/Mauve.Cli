using System;

namespace Mauve.Cli.Validation;

/// <summary>The result of a value or cross-value validation.</summary>
public readonly record struct ValueValidationOutcome
{
    private ValueValidationOutcome(bool succeeded, string? failureCode)
    {
        Succeeded = succeeded;
        FailureCode = failureCode;
    }

    /// <summary>Gets whether validation succeeded.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the safe failure identifier, when validation failed.</summary>
    public string? FailureCode { get; }

    /// <summary>Gets the successful validation outcome.</summary>
    public static ValueValidationOutcome Success { get; } = new(true, null);

    /// <summary>Creates a failed validation outcome.</summary>
    /// <param name="failureCode">A bounded ASCII reason identifier.</param>
    /// <returns>A failed outcome.</returns>
    public static ValueValidationOutcome Failure(string failureCode)
    {
        ArgumentNullException.ThrowIfNull(failureCode);
        return !ValidationFailureCode.IsValid(failureCode)
            ? throw new InvalidOperationException("A validator returned an invalid failure code.")
            : new ValueValidationOutcome(false, failureCode);
    }
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
