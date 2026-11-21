namespace Mauve.Cli.Invocation;

/// <summary>Controls whether directive processing continues.</summary>
public readonly record struct DirectiveResult
{
    private DirectiveResult(bool shouldContinue, int exitCode)
    {
        ShouldContinue = shouldContinue;
        ExitCode = exitCode;
    }

    /// <summary>Gets whether later directives and normal command processing should continue.</summary>
    public bool ShouldContinue { get; }

    /// <summary>Gets the exit code used when processing stops.</summary>
    public int ExitCode { get; }

    /// <summary>Gets the result that continues with the next directive or command handler.</summary>
    public static DirectiveResult ContinueProcessing { get; } = new(true, 0);

    /// <summary>Stops processing and returns the specified exit code.</summary>
    /// <param name="exitCode">The exact exit code to return, including zero.</param>
    /// <returns>A result that stops all later processing.</returns>
    public static DirectiveResult StopProcessing(int exitCode) => new(false, exitCode);
}
