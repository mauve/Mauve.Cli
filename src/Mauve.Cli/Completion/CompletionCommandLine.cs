using System;
using System.Collections.Generic;
using System.Text;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Completion;

internal static class CompletionCommandLine
{
    internal static CompletionRequest CreateRequest(CommandModel model, string text, int cursor)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(text);
        if (cursor < 0 || cursor > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(cursor));
        }

        if (text.Length > model.Limits.MaximumTotalCodeUnits)
        {
            throw new ArgumentException("The completion payload exceeds the configured input limit.", nameof(text));
        }

        List<Token> tokens = [];
        int index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            int start = index;
            StringBuilder value = new();
            char quote = '\0';
            int offset = 0;
            while (index < text.Length)
            {
                char character = text[index];
                if (quote == '\0' && char.IsWhiteSpace(character))
                {
                    break;
                }

                if ((character == '\'' || character == '"') && (quote == '\0' || quote == character))
                {
                    quote = quote == '\0' ? character : '\0';
                    index++;
                }
                else if (character == '\\'
                    && quote != '\''
                    && index + 1 < text.Length
                    && (quote == '\0' || text[index + 1] is '\\' or '"'))
                {
                    _ = value.Append(text[index + 1]);
                    index += 2;
                }
                else
                {
                    _ = value.Append(character);
                    index++;
                }

                if (index <= cursor)
                {
                    offset = value.Length;
                }
            }

            if (value.Length > model.Limits.MaximumTokenLength
                || tokens.Count >= model.Limits.MaximumArgumentCount + 1L)
            {
                throw new ArgumentException("The completion payload exceeds the configured input limit.", nameof(text));
            }

            tokens.Add(new Token(start, index, value.ToString(), offset));
        }

        if (tokens.Count == 0 || cursor <= tokens[0].End)
        {
            throw new ArgumentException("The completion cursor must follow the executable token.", nameof(cursor));
        }

        List<string> arguments = [];
        int activeIndex = -1;
        int activeOffset = 0;
        for (int tokenIndex = 1; tokenIndex < tokens.Count; tokenIndex++)
        {
            Token token = tokens[tokenIndex];
            if (activeIndex < 0 && cursor < token.Start)
            {
                activeIndex = arguments.Count;
                arguments.Add(string.Empty);
            }
            else if (activeIndex < 0 && cursor <= token.End)
            {
                activeIndex = arguments.Count;
                activeOffset = token.CursorOffset;
            }

            arguments.Add(token.Value);
        }

        if (activeIndex < 0)
        {
            activeIndex = arguments.Count;
        }

        return new CompletionRequest(model, arguments, activeIndex, activeOffset);
    }

    private sealed record Token(int Start, int End, string Value, int CursorOffset);
}
