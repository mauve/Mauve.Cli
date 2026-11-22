namespace Mauve.Cli.Definitions;

/// <summary>Describes how an option occurrence consumes and stores values.</summary>
public enum OptionArity
{
    /// <summary>The option consumes exactly one value.</summary>
    RequiredScalar,

    /// <summary>The option consumes a value only when supplied inline.</summary>
    OptionalScalar,

    /// <summary>The option is a boolean switch.</summary>
    BooleanFlag,

    /// <summary>Each occurrence contributes one element to a collection.</summary>
    RepeatedSingleCollection,

    /// <summary>An occurrence consumes one or more adjacent values.</summary>
    GreedyCollection,

    /// <summary>Each zero-value occurrence increments an integer count.</summary>
    Counting
}
