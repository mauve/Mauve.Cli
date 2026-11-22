namespace Mauve.Cli.Parsing;

/// <summary>Identifies a deterministic input or model diagnostic category.</summary>
public enum ParseDiagnosticCode
{
    /// <summary>The model contains an unsupported or invalid definition.</summary>
    InvalidModel,

    /// <summary>A token did not identify a declared child command.</summary>
    UnknownCommand,

    /// <summary>A token did not identify a visible option.</summary>
    UnknownOption,

    /// <summary>A token could not be assigned to a positional argument.</summary>
    UnexpectedArgument,

    /// <summary>A required option value was not supplied.</summary>
    MissingValue,

    /// <summary>An option spelling cannot accept the supplied value form.</summary>
    UnexpectedValue,

    /// <summary>A scalar option was supplied more than once.</summary>
    DuplicateOption,

    /// <summary>A supplied value could not be converted.</summary>
    InvalidValue,

    /// <summary>A declared default or validation rule rejected a value.</summary>
    ValidationFailed,

    /// <summary>A required option or positional argument was absent.</summary>
    MissingRequired,

    /// <summary>An option occurrence was consumed outside its final command scope.</summary>
    OptionOutOfScope,

    /// <summary>An input vector or token exceeded a documented limit.</summary>
    InputLimitExceeded,

    /// <summary>The diagnostic list reached its configured bound.</summary>
    DiagnosticLimitExceeded,

    /// <summary>A directive name is not registered on the model root.</summary>
    UnknownDirective,

    /// <summary>A directive bracket span is malformed.</summary>
    MalformedDirective,

    /// <summary>A required directive value is missing.</summary>
    MissingDirectiveValue,

    /// <summary>A directive value could not be converted.</summary>
    InvalidDirectiveValue,

    /// <summary>Mutually exclusive options were supplied together.</summary>
    ConflictingOptions
}
