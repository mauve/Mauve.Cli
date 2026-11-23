namespace Mauve.Cli.Definitions;

/// <summary>Controls whether an option may receive a value from an environment variable.</summary>
public enum EnvironmentVariableBinding
{
    /// <summary>Use the host's default option-binding policy.</summary>
    Inherit,

    /// <summary>Allow environment-variable binding when enabled by the host.</summary>
    Enabled,

    /// <summary>Do not bind this option from an environment variable.</summary>
    Disabled
}
