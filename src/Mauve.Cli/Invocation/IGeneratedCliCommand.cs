using Mauve.Cli.Definitions;

namespace Mauve.Cli.Invocation;

/// <summary>Identifies a command root with a source-generated model factory.</summary>
public interface IGeneratedCliCommand
{
    /// <summary>Creates the validated model for this generated command root.</summary>
    /// <param name="limits">Optional model and parsing limits.</param>
    /// <param name="includeStandardHelp">Whether to add the standard help option.</param>
    /// <returns>The validated command model.</returns>
    static abstract CommandModel CreateModel(CliLimits? limits = null, bool includeStandardHelp = false);
}

/// <summary>Identifies a generated command root whose entire command tree is synchronous.</summary>
public interface ISynchronousGeneratedCliCommand : IGeneratedCliCommand
{
}
