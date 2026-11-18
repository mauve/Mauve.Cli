using System;

namespace Mauve.Cli.Validation;

/// <summary>Validates one typed value after conversion and default application.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="value">The value to validate.</param>
/// <param name="context">Read-only information about the current parse.</param>
/// <returns>The validation outcome.</returns>
public delegate ValueValidationOutcome ValueValidatorCallback<T>(T? value, ValidationContext context);

/// <summary>Represents a value validator with an explicit target type.</summary>
public interface IValueValidator
{
    /// <summary>Gets the value type accepted by this validator.</summary>
    Type ValueType { get; }

    /// <summary>Validates one converted value.</summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="context">Read-only information about the current parse.</param>
    /// <returns>The validation outcome.</returns>
    ValueValidationOutcome Validate(object? value, ValidationContext context);
}

/// <summary>A typed value validator.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class ValueValidator<T> : IValueValidator
{
    private readonly ValueValidatorCallback<T> _callback;

    /// <summary>Creates a typed validator.</summary>
    /// <param name="callback">The validation callback.</param>
    public ValueValidator(ValueValidatorCallback<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    /// <inheritdoc />
    public Type ValueType => typeof(T);

    /// <inheritdoc />
    public ValueValidationOutcome Validate(object? value, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return value is T typedValue
            ? _callback(typedValue, context)
            : value is null && default(T) is null
                ? _callback(default, context)
                : throw new InvalidOperationException("The validator value type does not match the definition.");
    }
}

/// <summary>Validates relationships among values of one selected command.</summary>
/// <param name="context">Read-only values and presence information for the selected command.</param>
/// <returns>The validation outcome.</returns>
public delegate ValueValidationOutcome CrossValueValidator(ValidationContext context);
