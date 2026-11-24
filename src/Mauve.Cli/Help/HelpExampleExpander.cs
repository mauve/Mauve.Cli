using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mauve.Cli.Help;

internal static class HelpExampleExpander
{
    public static string Expand(string example, IReadOnlyList<string> path, string executableName, int initialPathLength = 1)
    {
        string text = HelpTextSanitizer.SingleLine(example).Trim();
        bool hasExecutable = StartsWithToken(text, "$0");
        string commandText = hasExecutable ? text[2..].TrimStart() : text;
        string fullPath = string.Join(' ', hasExecutable ? path.Skip(1) : path);
        if (path.Count > 1
            && !StartsWithToken(commandText, fullPath)
            && StartsWithToken(commandText, path[^1]))
        {
            string ancestors = string.Join(' ', hasExecutable ? path.Skip(1).SkipLast(1) : path.SkipLast(1));
            if (ancestors.Length > 0)
            {
                commandText = string.Concat(ancestors, " ", commandText);
            }
        }

        if (hasExecutable && initialPathLength > 1)
        {
            string mappedPrefix = string.Join(' ', path.Skip(1).Take(initialPathLength - 1));
            if (StartsWithToken(commandText, mappedPrefix))
            {
                commandText = commandText[mappedPrefix.Length..].TrimStart();
            }
        }

        text = hasExecutable ? string.Concat("$0", commandText.Length > 0 ? " " : string.Empty, commandText) : commandText;
        string executable = HelpTextSanitizer.SingleLine(executableName);
        if (executable.Length > 30)
        {
            int separator = executable.LastIndexOfAny(['/', '\\']);
            if (separator < executable.Length - 1)
            {
                executable = string.Concat(".../", executable.AsSpan(separator + 1));
            }
        }
        StringBuilder result = new();
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '$'
                && index + 1 < text.Length && text[index + 1] == '0'
                && (index == 0 || char.IsWhiteSpace(text[index - 1]))
                && (index + 2 == text.Length || char.IsWhiteSpace(text[index + 2])))
            {
                _ = result.Append(executable);
                index++;
            }
            else
            {
                _ = result.Append(text[index]);
            }
        }

        return result.ToString();
    }

    private static bool StartsWithToken(string text, string token)
        => text.StartsWith(token, StringComparison.Ordinal)
            && (text.Length == token.Length || char.IsWhiteSpace(text[token.Length]));
}
