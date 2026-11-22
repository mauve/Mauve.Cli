using System;
using System.Globalization;
using System.Text;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Completion;

internal static class ShellCompletion
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    internal static string CreateScript(string command, string shell)
    {
        ValidateCommand(command);
        string function = Convert.ToHexString(Encoding.UTF8.GetBytes(command));
        return shell switch
        {
            "bash" => $$"""
            _mauve_completion_{{function}}() {
                local prefix line candidate escaped mode=bash
                prefix="${COMP_LINE:0:COMP_POINT}"
                [[ "$COMP_WORDBREAKS" != *"="* && "${COMP_WORDS[COMP_CWORD]}" == *=* ]] && mode=bash-full
                local -a records decoded
                COMPREPLY=()
                mapfile -t records < <(command '{{command}}' '[completion:query-v1]' -- "$mode" "$prefix" "$COMP_LINE")
                for line in "${records[@]}"; do
                    [[ "$line" == x* ]] || return
                    candidate="$(printf '%s' "${line#x}" | base64 --decode 2>/dev/null || printf '%s' "${line#x}" | base64 -D)" || return
                    case "${COMP_WORDS[COMP_CWORD]}" in
                        \"*)
                            escaped=${candidate//\\/\\\\}
                            escaped=${escaped//\"/\\\"}
                            escaped=${escaped//\$/\\\$}
                            escaped=${escaped//\`/\\\`}
                            ;;
                        \'*)
                            escaped=${candidate//\'/\'\\\'\'}
                            ;;
                        *) printf -v escaped '%q' "$candidate" ;;
                    esac
                    decoded+=("$escaped")
                done
                COMPREPLY=("${decoded[@]}")
                compopt -o noquote
            }
            complete -F _mauve_completion_{{function}} -- '{{command}}'

            """,
            "zsh" => $$"""
            _mauve_completion_{{function}}() {
                local prefix="${BUFFER[1,CURSOR]}" line candidate
                local -a records candidates
                records=("${(@f)$(command '{{command}}' '[completion:query-v1]' -- zsh "$prefix" "$BUFFER")}")
                for line in "${records[@]}"; do
                    [[ -n "$line" ]] || continue
                    [[ "$line" == x* ]] || return
                    candidate="$(printf '%s' "${line#x}" | base64 --decode 2>/dev/null || printf '%s' "${line#x}" | base64 -D)" || return
                    candidates+=("$candidate")
                done
                (( ${#candidates} )) && compadd -a candidates
            }
            (( $+functions[compdef] )) || { autoload -Uz compinit && compinit; }
            compdef _mauve_completion_{{function}} '{{command}}'

            """,
            "fish" => $$"""
            function __mauve_completion_{{function}}
                set -l prefix (commandline -bc)
                set -l buffer (commandline -b)
                set -l candidates
                for line in (command '{{command}}' '[completion:query-v1]' -- fish "$prefix" "$buffer")
                    string match -q 'x*' -- "$line"; or return 1
                    set -l encoded (string sub -s 2 -- "$line")
                    set -l candidate (begin
                        printf '%s' "$encoded" | base64 --decode 2>/dev/null; or printf '%s' "$encoded" | base64 -D
                    end); or return 1
                    set -a candidates "$candidate"
                end
                for candidate in $candidates
                    printf '%s\n' "$candidate"
                end
            end
            complete -c '{{command}}' -f -a '(__mauve_completion_{{function}})'

            """,
            "powershell" => $$"""
            Register-ArgumentCompleter -Native -CommandName '{{command}}' -ScriptBlock {
                param($wordToComplete, $commandAst, $cursorPosition)
                $line = $commandAst.Extent.Text
                $cursor = $cursorPosition - $commandAst.Extent.StartOffset
                if ($cursor -lt 0 -or $cursor -gt $line.Length) { return }
                $prefix = $line.Substring(0, $cursor)
                $records = & '{{command}}' '[completion:query-v1]' '--' 'powershell' $prefix $line
                if ($LASTEXITCODE -ne 0) { return }
                $ErrorActionPreference = 'Stop'
                $results = @(foreach ($record in $records) {
                    if (-not $record.StartsWith('x')) { throw 'Invalid completion response.' }
                    $candidate = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($record.Substring(1)))
                    $escaped = "'" + $candidate.Replace("'", "''") + "'"
                    [System.Management.Automation.CompletionResult]::new($escaped, $candidate, 'ParameterValue', $candidate)
                })
                $results
            }

            """,
            _ => throw new ArgumentException("The completion shell is unsupported.", nameof(shell))
        };
    }

    internal static CompletionRequest CreateRequest(CommandModel model, string prefix, string line, string shell)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(line);
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The completion cursor is invalid.", nameof(prefix));
        }

        ValidateInput(line, shell);
        if (shell is "bash" or "bash-full")
        {
            ValidateBashActiveWord(prefix, shell == "bash-full");
        }

        CompletionRequest request = CompletionCommandLine.CreateRequest(model, line, prefix.Length);
        return shell != "powershell" && request.CursorTokenIndex < request.Arguments.Count
            && request.CursorCharacterOffset != request.Arguments[request.CursorTokenIndex].Length
            ? throw new ArgumentException("The shell cannot replace a suffix after the completion cursor.", nameof(prefix))
            : request;
    }

    internal static string EncodeCandidate(CompletionCandidate candidate, CompletionRequest request, bool fullToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        string text = candidate.InsertionText;
        if (fullToken && request.CursorTokenIndex < request.Arguments.Count)
        {
            string active = request.Arguments[request.CursorTokenIndex];
            text = string.Concat(
                active.AsSpan(0, candidate.ReplacementStart),
                candidate.InsertionText,
                active.AsSpan(candidate.ReplacementStart + candidate.ReplacementLength));
        }

        foreach (char character in text)
        {
            if (char.IsControl(character))
            {
                throw new ArgumentException("Completion candidates cannot contain control characters.", nameof(candidate));
            }
        }

        return "x" + Convert.ToBase64String(_strictUtf8.GetBytes(text));
    }

    internal static void ValidateCommand(string command)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        if (command.Length > 255 || command[0] == '-')
        {
            throw new ArgumentException("The completion command name must be at most 255 characters and cannot begin with a dash.", nameof(command));
        }

        foreach (char character in command)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')
            {
                throw new ArgumentException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The completion command name contains unsupported character U+{(int)character:X4}."), nameof(command));
            }
        }
    }

    private static void ValidateInput(string line, string shell)
    {
        char quote = '\0';
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (char.IsControl(character))
            {
                throw new ArgumentException("Control characters are not supported in shell completion input.", nameof(line));
            }

            if (character == '\\')
            {
                if (shell == "powershell" && quote != '\'')
                {
                    throw new ArgumentException("PowerShell escape syntax is unsupported.", nameof(line));
                }

                if (quote == '\''
                    && shell == "fish"
                    && index + 1 < line.Length
                    && line[index + 1] is '\\' or '\'')
                {
                    throw new ArgumentException("Fish single-quote escapes are unsupported.", nameof(line));
                }

                if (quote != '\'' && index + 1 < line.Length)
                {
                    char next = line[index + 1];
                    if (char.IsControl(next))
                    {
                        throw new ArgumentException("Control characters are not supported in shell completion input.", nameof(line));
                    }

                    if (quote == '"' && next is '$' or '`')
                    {
                        throw new ArgumentException("Shell-specific double-quote escapes are unsupported.", nameof(line));
                    }

                    if (quote == '\0' || next is '\\' or '"')
                    {
                        index++;
                    }
                }

                continue;
            }

            if (character is '\'' or '"' && (quote == '\0' || quote == character))
            {
                if (shell == "powershell" && quote == character
                    && index + 1 < line.Length && line[index + 1] == character)
                {
                    throw new ArgumentException("PowerShell doubled quote escapes are unsupported.", nameof(line));
                }

                quote = quote == '\0' ? character : '\0';
                continue;
            }

            if ((quote != '\'' && character is '$' or '`')
                || (quote == '\0' && character is ';' or '|' or '&' or '<' or '>' or '(' or ')' or '*' or '?' or '[' or ']' or '{' or '}' or '~' or '#' or '!'))
            {
                throw new ArgumentException("Shell expansion and operators are unsupported in completion input.", nameof(line));
            }
        }
    }

    private static void ValidateBashActiveWord(string prefix, bool fullToken)
    {
        char quote = '\0';
        bool quoted = false;
        bool escaped = false;
        bool unsupported = false;
        int equalsCount = 0;
        int tokenStart = 0;
        for (int index = 0; index < prefix.Length; index++)
        {
            char character = prefix[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && quote != '\'')
            {
                unsupported |= quote != '\0';
                escaped = true;
            }
            else if (character is '\'' or '"' && (quote == '\0' || quote == character))
            {
                quoted = true;
                if (quote == '\0' && index != tokenStart && prefix[index - 1] != '=')
                {
                    unsupported = true;
                }

                quote = quote == '\0' ? character : '\0';
            }
            else if (quote == '\0' && character == ':')
            {
                unsupported = true;
            }
            else if (quote == '\0' && character == '=')
            {
                equalsCount++;
                unsupported |= equalsCount > 1;
            }
            else if (quote == '\0' && char.IsWhiteSpace(character))
            {
                quoted = false;
                unsupported = false;
                equalsCount = 0;
                tokenStart = index + 1;
            }
        }

        if (unsupported || escaped || (quoted && (quote == '\0' || fullToken)))
        {
            throw new ArgumentException("Bash cannot safely complete this active word.", nameof(prefix));
        }
    }
}
