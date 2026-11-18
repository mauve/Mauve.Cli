namespace Mauve.Cli.Definitions;

/// <summary>Controls whether an option is visible only to its owner or inherited by descendants.</summary>
public enum OptionScope
{
    /// <summary>The option is visible only while its declaring command is selected.</summary>
    Local,

    /// <summary>The option is visible to the declaring command and its descendants.</summary>
    Inherited
}
