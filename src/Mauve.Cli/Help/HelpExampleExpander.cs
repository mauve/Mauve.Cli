using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mauve.Cli.Help;

internal static class HelpExampleExpander
{
    public static string Expand(string example, IReadOnlyList<string> path, string executableName)
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

        text = hasExecutable ? string.Concat("$0", commandText.Length > 0 ? " " : string.Empty, commandText) : commandText;
        string executable = HelpTextSanitizer.SingleLine(executableName);
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
