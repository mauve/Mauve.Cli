using System;

namespace Mauve.Cli.Validation;

/// <summary>Signals an expected validation rejection, not an application fault.</summary>
public sealed class CliValidationException : Exception
{
    /// <summary>Creates a rejection with a generic failure code.</summary>
    public CliValidationException()
        : this(ValueValidationOutcome.Failure("validation.failed"))
    {
    }

    /// <summary>Creates a rejection with a generic failure code and an application message.</summary>
    /// <param name="message">Application-owned exception text, not rendered by the parser.</param>
    public CliValidationException(string? message)
        : base(message)
    {
        Outcome = ValueValidationOutcome.Failure("validation.failed");
    }

    /// <summary>Creates a rejection with an application message and inner exception.</summary>
    /// <param name="message">Application-owned exception text, not rendered by the parser.</param>
    /// <param name="innerException">The original application exception.</param>
    public CliValidationException(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Outcome = ValueValidationOutcome.Failure("validation.failed");
    }

    /// <summary>Creates a rejection carrying a structured failed outcome.</summary>
    /// <param name="outcome">The failed outcome.</param>
    public CliValidationException(ValueValidationOutcome outcome)
        : base("A value failed validation.")
    {
        if (outcome.Succeeded || !ValidationFailureCode.IsValid(outcome.FailureCode ?? string.Empty))
        {
            throw new ArgumentException("A validation exception requires a failed outcome.", nameof(outcome));
        }

        Outcome = outcome;
    }

    /// <summary>Gets the structured rejection.</summary>
    public ValueValidationOutcome Outcome { get; }
}
