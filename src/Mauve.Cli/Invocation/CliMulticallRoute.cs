using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mauve.Cli.Invocation;

/// <summary>Maps an executable name to a command path in a supplied multicall model.</summary>
public sealed class CliMulticallRoute
{
    private readonly ReadOnlyCollection<string> _commandPath;

    /// <summary>Creates an immutable executable-to-command-path route.</summary>
    /// <param name="executableName">A single executable filename, without directory components.</param>
    /// <param name="commandPath">The root name or alias followed by zero or more child names or aliases.</param>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentException">The executable name or path is empty or malformed.</exception>
    public CliMulticallRoute(string executableName, IReadOnlyList<string> commandPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        ArgumentNullException.ThrowIfNull(commandPath);
        if (executableName is "." or ".."
            || executableName.IndexOfAny(['/', '\\']) >= 0
            || ContainsControlCharacter(executableName))
        {
            throw new ArgumentException("A multicall executable name must be a filename without directory components or control characters.", nameof(executableName));
        }

        if (commandPath.Count == 0)
        {
            throw new ArgumentException("A multicall command path must identify a model root.", nameof(commandPath));
        }

        string[] snapshot = new string[commandPath.Count];
        for (int index = 0; index < snapshot.Length; index++)
        {
            string segment = commandPath[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(segment, nameof(commandPath));
            if (ContainsControlCharacter(segment))
            {
                throw new ArgumentException("A multicall command path cannot contain control characters.", nameof(commandPath));
            }

            snapshot[index] = segment;
        }

        ExecutableName = executableName;
        _commandPath = Array.AsReadOnly(snapshot);
    }

    /// <summary>Gets the executable filename matched under platform-native casing and extension rules.</summary>
    public string ExecutableName { get; }

    /// <summary>Gets the immutable full command path, beginning with a supplied root name or alias.</summary>
    public IReadOnlyList<string> CommandPath => _commandPath;

    private static bool ContainsControlCharacter(string value)
    {
        foreach (char character in value)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}
