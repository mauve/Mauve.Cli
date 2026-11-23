using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Mauve.Cli.Validation;

/// <summary>Creates reusable, statically typed validation rules.</summary>
public static class CliValidators
{
    /// <summary>Creates an inclusive minimum validator.</summary>
    /// <typeparam name="T">The comparable value type.</typeparam>
    /// <param name="minimum">The inclusive minimum.</param>
    /// <returns>A typed value validator.</returns>
    public static ValueValidator<T> Minimum<T>(T minimum)
        where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(minimum);
        return new ValueValidator<T>((value, _) => value is null || value.CompareTo(minimum) >= 0
            ? ValueValidationOutcome.Success : ValueValidationOutcome.Failure("minimum.invalid",
                string.Create(CultureInfo.InvariantCulture, $"Value must be at least {minimum}.")));
    }

    /// <summary>Creates an inclusive maximum validator.</summary>
    /// <typeparam name="T">The comparable value type.</typeparam>
    /// <param name="maximum">The inclusive maximum.</param>
    /// <returns>A typed value validator.</returns>
    public static ValueValidator<T> Maximum<T>(T maximum)
        where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(maximum);
        return new ValueValidator<T>((value, _) => value is null || value.CompareTo(maximum) <= 0
            ? ValueValidationOutcome.Success : ValueValidationOutcome.Failure("maximum.invalid",
                string.Create(CultureInfo.InvariantCulture, $"Value must be at most {maximum}.")));
    }

    /// <summary>Creates an inclusive range validator.</summary>
    /// <typeparam name="T">The comparable value type.</typeparam>
    /// <param name="minimum">The inclusive minimum.</param>
    /// <param name="maximum">The inclusive maximum.</param>
    /// <returns>An ordered value validator.</returns>
    public static ValueValidator<T> Range<T>(T minimum, T maximum)
        where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(minimum);
        ArgumentNullException.ThrowIfNull(maximum);
        return minimum.CompareTo(maximum) > 0
            ? throw new ArgumentException("The minimum cannot exceed the maximum.")
            : new ValueValidator<T>((value, _) => value is null || (value.CompareTo(minimum) >= 0 && value.CompareTo(maximum) <= 0)
                ? ValueValidationOutcome.Success
                : ValueValidationOutcome.Failure("range.invalid",
                    string.Create(CultureInfo.InvariantCulture, $"Value must be between {minimum} and {maximum} (inclusive).")));
    }

    /// <summary>Creates a validator for nullable values using an underlying validator.</summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="validator">The non-null validator.</param>
    /// <returns>A nullable value validator.</returns>
    public static ValueValidator<T?> Nullable<T>(ValueValidator<T> validator)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(validator);
        return new ValueValidator<T?>((value, context) => value.HasValue
            ? validator.Validate(value.Value, context)
            : ValueValidationOutcome.Success);
    }

    /// <summary>Creates a string length validator using UTF-16 code units.</summary>
    /// <param name="minimum">The inclusive minimum length.</param>
    /// <param name="maximum">The inclusive maximum length.</param>
    /// <returns>A string validator.</returns>
    public static ValueValidator<string> StringLength(int minimum, int maximum)
    {
        ValidateLengths(minimum, maximum);
        return new ValueValidator<string>((value, _) => value is null || (value.Length >= minimum && value.Length <= maximum)
            ? ValueValidationOutcome.Success
            : ValueValidationOutcome.Failure("length.invalid", LengthMessage(minimum, maximum, collection: false)));
    }

    /// <summary>Creates a collection count validator.</summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="minimum">The inclusive minimum count.</param>
    /// <param name="maximum">The inclusive maximum count.</param>
    /// <returns>A collection validator.</returns>
    public static ValueValidator<IReadOnlyList<T>> CollectionLength<T>(int minimum, int maximum)
    {
        ValidateLengths(minimum, maximum);
        return new ValueValidator<IReadOnlyList<T>>((value, _) => value is null || (value.Count >= minimum && value.Count <= maximum)
            ? ValueValidationOutcome.Success
            : ValueValidationOutcome.Failure("length.invalid", LengthMessage(minimum, maximum, collection: true)));
    }

    /// <summary>Rejects empty or whitespace-only strings.</summary>
    /// <returns>A string validator.</returns>
    public static ValueValidator<string> NotEmpty() =>
        new(static (value, _) => !string.IsNullOrWhiteSpace(value)
            ? ValueValidationOutcome.Success
            : ValueValidationOutcome.Failure("empty.invalid", "Value must not be empty or whitespace."));

    /// <summary>Rejects empty collections.</summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <returns>A collection validator.</returns>
    public static ValueValidator<IReadOnlyList<T>> NotEmptyCollection<T>() =>
        new(static (value, _) => value is not null && value.Count > 0
            ? ValueValidationOutcome.Success
            : ValueValidationOutcome.Failure("empty.invalid", "Collection must contain at least one item."));

    /// <summary>Creates a validator for an ordinal set of allowed string values.</summary>
    /// <param name="values">Distinct allowed values.</param>
    /// <returns>A string validator that also supplies completion metadata.</returns>
    public static IValueValidator AllowedValues(params string[] values) => new AllowedValuesValidator(values);

    /// <summary>Adapts a static validator provider.</summary>
    /// <typeparam name="T">The provider's value type.</typeparam>
    /// <typeparam name="TProvider">The static provider.</typeparam>
    /// <returns>A typed validator.</returns>
    public static ValueValidator<T> Provider<T, TProvider>()
        where TProvider : ICliValidator<T> => new(TProvider.Validate);

    /// <summary>Applies an element validator to each item of a collection.</summary>
    /// <typeparam name="T">The collection element type.</typeparam>
    /// <param name="validator">The validator for each item.</param>
    /// <returns>A collection validator.</returns>
    public static IValueValidator Each<T>(IValueValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        return validator.ValueType != typeof(T)
            ? throw new ArgumentException("The item validator type does not match.", nameof(validator))
            : new EachValidator<T>(validator);
    }

    private static string LengthMessage(int minimum, int maximum, bool collection)
    {
        string subject = collection ? "Collection count" : "String length";
        return maximum == int.MaxValue
            ? string.Create(CultureInfo.InvariantCulture, $"{subject} must be at least {minimum}.")
            : minimum == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{subject} must be at most {maximum}.")
                : string.Create(CultureInfo.InvariantCulture, $"{subject} must be between {minimum} and {maximum} (inclusive).");
    }

    private static void ValidateLengths(int minimum, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
    }

    private sealed class AllowedValuesValidator : IValueValidator, IAllowedValues
    {
        private readonly ReadOnlyCollection<string> _values;

        public AllowedValuesValidator(string[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Length is 0 or > 512 || values.Any(static value => value is null || value.Length > 512)
                || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            {
                throw new ArgumentException("Allowed values must be distinct and within the supported size limits.", nameof(values));
            }

            _values = Array.AsReadOnly((string[])values.Clone());
        }

        public Type ValueType => typeof(string);

        public IReadOnlyList<string> Values => _values;

        public ValueValidationOutcome Validate(object? value, ValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return value is not (null or string)
                ? throw new InvalidOperationException("The validator value type does not match the definition.")
                : value is string text && _values.Contains(text, StringComparer.Ordinal)
                    ? ValueValidationOutcome.Success
                    : ValueValidationOutcome.Failure("allowed.invalid",
                        $"Value must be one of: {string.Join(", ", _values.Select(static item => $"'{item}'"))} (case-sensitive).");
        }
    }

    private sealed class EachValidator<T>(IValueValidator validator) : IValueValidator, IAllowedValues
    {
        public Type ValueType => typeof(IReadOnlyList<T>);

        public IReadOnlyList<string> Values => (validator as IAllowedValues)?.Values ?? [];

        public ValueValidationOutcome Validate(object? value, ValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (value is null)
            {
                return ValueValidationOutcome.Success;
            }

            if (value is not IReadOnlyList<T> items)
            {
                throw new InvalidOperationException("The validator value type does not match the definition.");
            }

            for (int index = 0; index < items.Count; index++)
            {
                ValueValidationOutcome outcome;
                try
                {
                    outcome = validator.Validate(items[index], context);
                }
                catch (CliValidationException exception)
                {
                    outcome = exception.Outcome;
                }

                if (!outcome.Succeeded)
                {
                    return outcome.Target is not null || outcome.ItemIndex is not null
                        ? throw new InvalidOperationException("An item validator returned an invalid target.")
                        : outcome.WithItemIndex(index);
                }
            }

            return ValueValidationOutcome.Success;
        }
    }
}

internal interface IAllowedValues
{
    IReadOnlyList<string> Values { get; }
}
