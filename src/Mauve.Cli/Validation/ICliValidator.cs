namespace Mauve.Cli.Validation;

/// <summary>Provides validation through a statically referenced implementation.</summary>
/// <typeparam name="T">The type accepted by the provider.</typeparam>
public interface ICliValidator<T>
{
    /// <summary>Validates an assembled value.</summary>
    /// <param name="value">The value being validated.</param>
    /// <param name="context">The current read-only validation context.</param>
    /// <returns>A success or a structured rejection.</returns>
    static abstract ValueValidationOutcome Validate(T? value, ValidationContext context);
}
