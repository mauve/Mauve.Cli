using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using SharpFuzz;

namespace Mauve.Cli.Fuzzing;

internal static class Program
{
    private const int MaximumInputBytes = 8192;
    private static readonly CliLimits Limits = RichModel.Limits;
    private static readonly CommandModel CompletionModel = CommandModel.Create(new CommandBuilder("tool"), Limits);
    private static readonly CommandModel RelaxedCompletionModel = CommandModel.Create(
        new CommandBuilder("tool"),
        new CliLimits(maximumArgumentCount: 64, maximumTokenLength: 1024,
            maximumTotalCodeUnits: 1024, maximumDiagnostics: 8));
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static int Main(string[] args)
    {
        if (args.Length >= 2)
        {
            string target = ValidateTarget(args[0]);
            int replayed = 0;
            foreach (string path in args.Skip(1))
            {
                IEnumerable<string> files = Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                    : [path];
                foreach (string file in files)
                {
                    try
                    {
                        Replay(target, file);
                        replayed++;
                    }
                    catch (Exception) when (ReportFailedInput(file))
                    {
                        throw;
                    }
                }
            }

            Console.Error.WriteLine($"Replayed {replayed} {target} input(s).");
            return 0;
        }

        if (args.Length != 0)
        {
            throw new ArgumentException("Use <target> <input-file-or-directory>... for replay, or MAUVE_FUZZ_TARGET for libFuzzer.");
        }

        string selected = ValidateTarget(Environment.GetEnvironmentVariable("MAUVE_FUZZ_TARGET"));
        Fuzzer.LibFuzzer.Run(data => Run(selected, data));
        return 0;
    }

    private static bool ReportFailedInput(string file)
    {
        Console.Error.WriteLine($"Replay failed for input: {file}");
        return false;
    }

    private static void Replay(string target, string path)
    {
        using FileStream file = File.OpenRead(path);
        byte[] data = new byte[MaximumInputBytes];
        int length = 0;
        while (length < data.Length)
        {
            int read = file.Read(data, length, data.Length - length);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        Run(target, data.AsSpan(0, length));
    }

    private static string ValidateTarget(string? target)
        => target is "completion" or "shell" or "csv" or "argv"
            ? target
            : throw new ArgumentException("Target must be completion, shell, csv, or argv.");

    private static void Run(string target, ReadOnlySpan<byte> data)
    {
        Input input = new(data[..Math.Min(data.Length, MaximumInputBytes)]);
        switch (target)
        {
            case "completion":
                Completion(ref input);
                break;
            case "shell":
                Shell(ref input);
                break;
            case "csv":
                Csv(ref input);
                break;
            case "argv":
                Parser(ref input);
                break;
        }
    }

    private static void Completion(ref Input input)
    {
        int cursorByte = input.Byte();
        string line = "tool " + input.Text();
        int extra = input.Byte() % 9;
        for (int i = 0; i < extra; i++)
        {
            line += " " + input.Token();
        }

        int cursor = 5 + (cursorByte % (line.Length - 4));
        CompletionRequest? first = null;
        try
        {
            first = CompletionRequest.ParseCommandLine(CompletionModel, line, cursor);
        }
        catch (ArgumentException exception) when (exception.GetType() == typeof(ArgumentException) && exception.ParamName == "text")
        {
            CompletionRequest relaxed = CompletionRequest.ParseCommandLine(RelaxedCompletionModel, line, line.Length);
            Check(relaxed.Arguments.Count > Limits.MaximumArgumentCount
                || relaxed.Arguments.Any(static argument => argument.Length > Limits.MaximumTokenLength), "unexpected completion input limit");
        }

        if (first is not null)
        {
            CompletionRequest second = CompletionRequest.ParseCommandLine(CompletionModel, line, cursor);
            Check(first.CursorTokenIndex >= 0 && first.CursorTokenIndex <= first.Arguments.Count, "completion index");
            int activeLength = first.CursorTokenIndex == first.Arguments.Count ? 0 : first.Arguments[first.CursorTokenIndex].Length;
            Check(first.CursorCharacterOffset >= 0 && first.CursorCharacterOffset <= activeLength, "completion offset");
            Check(first.CursorTokenIndex == second.CursorTokenIndex
                && first.CursorCharacterOffset == second.CursorCharacterOffset
                && first.Arguments.SequenceEqual(second.Arguments, StringComparer.Ordinal), "completion determinism");
        }

        if (first is not null)
        {
            CompleteWithService(line, cursor);
        }

        string logical = line[5..];
        string quoted = "tool \"" + logical.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        if (quoted.Length <= Limits.MaximumTotalCodeUnits && logical.Length <= Limits.MaximumTokenLength)
        {
            CompletionRequest roundTrip = CompletionRequest.ParseCommandLine(CompletionModel, quoted, quoted.Length);
            Check(roundTrip.Arguments.Count == 1 && roundTrip.Arguments[0] == logical
                && roundTrip.CursorTokenIndex == 0 && roundTrip.CursorCharacterOffset == logical.Length, "completion quoted round trip");
        }

        try
        {
            _ = CompletionRequest.ParseCommandLine(CompletionModel, line, -1);
            throw new InvalidOperationException("Invalid completion cursor accepted.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static void CompleteWithService(string line, int cursor)
    {
        RichModel rich = RichModel.Instance;
        CompletionRequest request;
        try
        {
            request = CompletionRequest.ParseCommandLine(rich.Model, line, cursor);
        }
        catch (ArgumentException exception) when (exception.GetType() == typeof(ArgumentException) && exception.ParamName == "text")
        {
            return;
        }

        CompletionService service = new();
        CompletionResult result = service.CompleteAsync(request, rich.Providers).AsTask().GetAwaiter().GetResult();
        CompletionResult repeated = service.CompleteAsync(request, rich.Providers).AsTask().GetAwaiter().GetResult();
        Check(Enum.IsDefined(result.Outcome), "completion outcome");
        Check(result.Outcome == repeated.Outcome && result.IsComplete == repeated.IsComplete
            && result.Candidates.Select(static c => (c.InsertionText, c.ReplacementStart, c.ReplacementLength, c.Kind))
                .SequenceEqual(repeated.Candidates.Select(static c => (c.InsertionText, c.ReplacementStart, c.ReplacementLength, c.Kind))),
            "completion service determinism");
        Check(result.Candidates.Count <= 128, "completion candidate count");
        string active = request.CursorTokenIndex == request.Arguments.Count
            ? string.Empty : request.Arguments[request.CursorTokenIndex];
        foreach (CompletionCandidate candidate in result.Candidates)
        {
            Check(candidate.InsertionText.Length is > 0 and <= 1024 && Enum.IsDefined(candidate.Kind), "completion candidate text");
            Check(candidate.ReplacementStart >= 0 && candidate.ReplacementLength >= 0
                && candidate.ReplacementStart <= active.Length
                && candidate.ReplacementLength <= active.Length - candidate.ReplacementStart, "completion replacement range");
        }

        CompletionResult cancelled = service.CompleteAsync(request, rich.Providers, new System.Threading.CancellationToken(true))
            .AsTask().GetAwaiter().GetResult();
        Check(cancelled.Outcome == CompletionOutcome.Cancelled && cancelled.Candidates.Count == 0, "completion cancellation");
    }

    private static void Shell(ref Input input)
    {
        string[] shells = ["bash", "bash-full", "zsh", "fish", "powershell"];
        string shell = shells[input.Byte() % shells.Length];
        string prefix = "tool --value=" + input.Text();
        string line = prefix + input.Text();
        string candidate = input.Text();
        OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(value, (_, _) => Candidate(candidate))
            .Build();
        int dispatched = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(value)
            .AddDirective(PredefinedDirectives.CreateCompletion("tool", providers: providers))
            .SetHandler((_, _) => { dispatched++; return 0; }), Limits);
        using StringWriter output = new();
        using StringWriter errors = new();
        int code = CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", shell, prefix, line],
            new CliHostOptions(output, errors)).GetAwaiter().GetResult();
        Check(dispatched == 0, "shell dispatched command");
        string records = output.ToString();
        output.GetStringBuilder().Clear();
        errors.GetStringBuilder().Clear();
        int candidateCode = CliInvoker.RunAsync(model,
            ["[completion:query-v1]", "--", shell, "tool --value=", "tool --value="],
            new CliHostOptions(output, errors)).GetAwaiter().GetResult();
        bool validCandidate = !candidate.Any(char.IsControl);
        try
        {
            _ = StrictUtf8.GetByteCount(candidate);
        }
        catch (EncoderFallbackException)
        {
            validCandidate = false;
        }

        string expectedCandidate = shell == "bash" ? candidate : "--value=" + candidate;
        if (validCandidate)
        {
            string expectedRecord = "x" + Convert.ToBase64String(StrictUtf8.GetBytes(expectedCandidate)) + Environment.NewLine;
            Check(candidateCode == 0 && output.ToString() == expectedRecord, "shell candidate encoding");
        }
        else
        {
            Check(candidateCode == 2 && output.ToString().Length == 0, "unsafe shell candidate");
        }

        Check(dispatched == 0, "shell candidate dispatched command");
        if (code != 0)
        {
            Check(records.Length == 0, "shell failure emitted candidates");
            return;
        }

        CompletionRequest request = CompletionRequest.ParseCommandLine(model, line, prefix.Length);
        CompletionResult expectedResult = new CompletionService().CompleteAsync(request, providers).AsTask().GetAwaiter().GetResult();
        Check(expectedResult.Outcome == CompletionOutcome.Completed && expectedResult.IsComplete, "shell completion outcome");
        string[] lines = records.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Check(lines.Length == expectedResult.Candidates.Count
            && records == string.Concat(lines.Select(static item => item + Environment.NewLine)), "shell candidate framing");
        for (int i = 0; i < lines.Length; i++)
        {
            string record = lines[i];
            Check(record.Length > 0 && record[0] == 'x', "shell record prefix");
            byte[] bytes = Convert.FromBase64String(record[1..]);
            Check(Convert.ToBase64String(bytes) == record[1..], "shell base64 canonical");
            string decoded = StrictUtf8.GetString(bytes);
            CompletionCandidate expectedCandidateRecord = expectedResult.Candidates[i];
            string active = request.CursorTokenIndex == request.Arguments.Count
                ? string.Empty : request.Arguments[request.CursorTokenIndex];
            int start = expectedCandidateRecord.ReplacementStart;
            int length = expectedCandidateRecord.ReplacementLength;
            Check(start >= 0 && length >= 0 && start <= active.Length && length <= active.Length - start, "shell replacement range");
            string expected = shell == "bash" ? expectedCandidateRecord.InsertionText
                : active[..start] + expectedCandidateRecord.InsertionText + active[(start + length)..];
            Check(decoded == expected, "shell replacement");
        }
    }

    private static async IAsyncEnumerable<CompletionValue> Candidate(string value)
    {
        yield return new CompletionValue(value);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static void Csv(ref Input input)
    {
        int selector = input.Byte();
        if ((selector & 0x80) != 0)
        {
            EnvironmentBinding(ref input);
            return;
        }

        bool encoded = (selector & 1) != 0;
        int count = 1 + (input.Byte() % 8);
        string[] fields = new string[count];
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] = input.Text();
        }

        string csv = encoded
            ? string.Join(",", fields.Select(static field => "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""))
            : fields[0];
        OptionDefinition items = new("--items", typeof(IReadOnlyList<string>), OptionArity.GreedyCollection, elementType: typeof(string));
        int dispatched = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(items)
            .SetHandler((_, _) => { dispatched++; return 0; }), Limits);
        CliHostOptions options = new(enableEnvironmentVariables: true,
            environmentVariableLookup: name => name == "TOOL_ITEMS" ? csv : null);
        InvocationResult result = new CliInvoker().InvokeAsync(model, [], options).AsTask().GetAwaiter().GetResult();
        ParseResult parsed = result.ParseResult ?? throw new InvalidOperationException("CSV parse result missing.");
        Check(parsed.Diagnostics.Count <= Limits.MaximumDiagnostics, "CSV diagnostic count");
        if (!encoded && !csv.Contains('"', StringComparison.Ordinal))
        {
            string[] expectedFields = csv.Split(',');
            if (expectedFields.Length <= Limits.MaximumArgumentCount)
            {
                Check(result.Outcome == InvocationOutcome.Completed && dispatched == 1
                    && parsed.TryGetOptionValue(items, out IReadOnlyList<string>? raw)
                    && raw is not null && raw.SequenceEqual(expectedFields, StringComparer.Ordinal), "raw CSV fields");
            }
        }

        if (encoded && csv.Length <= Limits.MaximumTokenLength)
        {
            Check(result.Outcome == InvocationOutcome.Completed && dispatched == 1, "encoded CSV rejected");
            Check(parsed.TryGetOptionValue(items, out IReadOnlyList<string>? actual)
                && actual is not null && actual.SequenceEqual(fields, StringComparer.Ordinal), "CSV round trip");
        }
        else if (result.Outcome == InvocationOutcome.InvalidInput)
        {
            Check(dispatched == 0 && parsed.Values.Count == 0, "invalid CSV dispatched");
            foreach (ParseDiagnostic diagnostic in parsed.Diagnostics)
            {
                Check(diagnostic.TokenIndex is null && diagnostic.ValueTokenIndex is null, "CSV coordinates");
                if (csv.Contains("fuzz-secret-", StringComparison.Ordinal))
                {
                    Check(!diagnostic.ToString(false).Contains("fuzz-secret-", StringComparison.Ordinal), "CSV disclosure");
                }
            }
        }
        else
        {
            Check(result.Outcome == InvocationOutcome.Completed && dispatched == 1, "CSV outcome");
        }

        dispatched = 0;
        string malformed = "fuzz-secret-,\"" + fields[0].Replace("\"", "\"\"", StringComparison.Ordinal);
        CliHostOptions malformedOptions = new(enableEnvironmentVariables: true,
            environmentVariableLookup: name => name == "TOOL_ITEMS" ? malformed : null);
        InvocationResult rejected = new CliInvoker().InvokeAsync(model, [], malformedOptions).AsTask().GetAwaiter().GetResult();
        ParseResult rejectedParse = rejected.ParseResult ?? throw new InvalidOperationException("CSV rejection result missing.");
        Check(rejected.Outcome == InvocationOutcome.InvalidInput && dispatched == 0 && rejectedParse.Values.Count == 0, "malformed CSV accepted");
        Check(rejectedParse.Diagnostics.Count > 0 && rejectedParse.Diagnostics.Count <= Limits.MaximumDiagnostics, "CSV rejection diagnostics");
        foreach (ParseDiagnostic diagnostic in rejectedParse.Diagnostics)
        {
            Check(diagnostic.TokenIndex is null && diagnostic.ValueTokenIndex is null
                && !diagnostic.ToString(false).Contains("fuzz-secret-", StringComparison.Ordinal), "CSV rejection disclosure");
        }
    }

    private static void EnvironmentBinding(ref Input input)
    {
        RichModel rich = RichModel.Instance;
        int mask = input.Byte() | (input.Byte() << 8);
        Dictionary<string, string> environment = [];
        for (int i = 0; i < rich.EnvironmentNames.Count; i++)
        {
            if ((mask & (1 << i)) == 0)
            {
                continue;
            }

            string value = input.Token();
            environment[rich.EnvironmentNames[i]] = rich.EnvironmentNames[i] == "TOOL_SECRET" ? "fuzz-secret-" + value : value;
        }

        string[] argv = new string[input.Byte() % 4];
        for (int i = 0; i < argv.Length; i++)
        {
            argv[i] = input.Token();
        }

        CliHostOptions Host() => new(new StringWriter(), new StringWriter(), enableEnvironmentVariables: true,
            environmentVariableLookup: key => environment.GetValueOrDefault(key));
        RichModel.Reset();
        InvocationResult result = new CliInvoker().InvokeAsync(rich.Model, argv, Host()).AsTask().GetAwaiter().GetResult();
        ParseResult parsed = result.ParseResult ?? throw new InvalidOperationException("Environment parse result missing.");
        int commands = RichModel.Commands;
        string[] diagnostics = [.. parsed.Diagnostics.Select(static d => d.ToString(false))];
        Check(parsed.Diagnostics.Count <= Limits.MaximumDiagnostics, "environment diagnostic count");
        Check(diagnostics.All(static d => !d.Contains("fuzz-secret-", StringComparison.Ordinal)), "environment secret disclosure");
        if (result.Outcome == InvocationOutcome.InvalidInput)
        {
            Check(commands == 0 && parsed.Values.Count == 0, "invalid environment dispatched");
            if (argv.Length == 0)
            {
                Check(parsed.Diagnostics.All(static d => d.TokenIndex is null && d.ValueTokenIndex is null), "environment coordinates");
            }
        }

        RichModel.Reset();
        InvocationResult repeated = new CliInvoker().InvokeAsync(rich.Model, argv, Host()).AsTask().GetAwaiter().GetResult();
        Check(repeated.Outcome == result.Outcome && RichModel.Commands == commands
            && diagnostics.SequenceEqual(repeated.ParseResult!.Diagnostics.Select(static d => d.ToString(false)), StringComparer.Ordinal),
            "environment determinism");

        int environmentCount = 1 + (input.Byte() % 100);
        string environmentName = input.Text();
        string explicitTag = input.Text();
        environment.Clear();
        environment["TOOL_COUNT"] = environmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        environment["TOOL_NAME"] = environmentName;
        environment["TOOL_TAG"] = "old,other";
        RichModel.Reset();
        InvocationResult bound = new CliInvoker().InvokeAsync(rich.Model, [], Host()).AsTask().GetAwaiter().GetResult();
        ParseResult boundParse = bound.ParseResult ?? throw new InvalidOperationException("Environment parse result missing.");
        Check(bound.Outcome == InvocationOutcome.Completed && RichModel.Commands == 1
            && boundParse.TryGetOptionValue(rich.Count, out int actualCount) && actualCount == environmentCount
            && boundParse.TryGetOptionValue(rich.Name, out string? actualName) && actualName == environmentName
            && boundParse.TryGetOptionValue(rich.Tags, out IReadOnlyList<string>? actualTags)
            && actualTags is not null && actualTags.SequenceEqual(["old", "other"], StringComparer.Ordinal),
            "environment typed values");
        foreach (OptionDefinition option in (OptionDefinition[])[rich.Count, rich.Name, rich.Tags])
        {
            ParsedValue entry = boundParse.Values.Single(value => ReferenceEquals(value.Option, option));
            Check(entry.Origin == ValueOrigin.Environment && entry.Presence == ValuePresence.WithValue
                && entry.TokenIndices.Count == 0, "environment provenance");
        }

        environment.Clear();
        RichModel.Reset();
        InvocationResult defaults = new CliInvoker().InvokeAsync(rich.Model, [], Host()).AsTask().GetAwaiter().GetResult();
        Check(defaults.Outcome == InvocationOutcome.Completed
            && defaults.ParseResult!.TryGetOptionValue(rich.Count, out int defaultCount) && defaultCount == 5
            && defaults.ParseResult.Values.Single(value => ReferenceEquals(value.Option, rich.Count)).Origin == ValueOrigin.Default,
            "environment default");

        environment["TOOL_COUNT"] = input.Text();
        environment["TOOL_TAG"] = input.Text();
        List<string> lookups = [];
        CliHostOptions tracked = new(new StringWriter(), new StringWriter(), enableEnvironmentVariables: true,
            environmentVariableLookup: key =>
            {
                lookups.Add(key);
                return environment.GetValueOrDefault(key);
            });
        RichModel.Reset();
        InvocationResult explicitOption = new CliInvoker().InvokeAsync(rich.Model, ["--count=7", "--tag=" + explicitTag], tracked)
            .AsTask().GetAwaiter().GetResult();
        Check(explicitOption.Outcome == InvocationOutcome.Completed
            && RichModel.Commands == 1
            && explicitOption.ParseResult!.TryGetOptionValue(rich.Count, out int explicitCount) && explicitCount == 7
            && explicitOption.ParseResult.TryGetOptionValue(rich.Tags, out IReadOnlyList<string>? explicitTags)
            && explicitTags is not null && explicitTags.SequenceEqual([explicitTag], StringComparer.Ordinal)
            && !lookups.Contains("TOOL_COUNT", StringComparer.Ordinal) && !lookups.Contains("TOOL_TAG", StringComparer.Ordinal),
            "explicit options override environment");
        foreach (OptionDefinition option in (OptionDefinition[])[rich.Count, rich.Tags])
        {
            ParsedValue entry = explicitOption.ParseResult!.Values.Single(value => ReferenceEquals(value.Option, option));
            Check(entry.Origin == ValueOrigin.Explicit && entry.Presence == ValuePresence.WithValue
                && entry.TokenIndices.Count > 0, "explicit provenance");
        }
    }

    private static void Parser(ref Input input)
    {
        int count = input.Byte() % 20;
        string[] argv = new string[count];
        for (int i = 0; i < argv.Length; i++)
        {
            argv[i] = input.Token();
        }

        RichModel rich = RichModel.Instance;
        CommandModel model = rich.Model;
        OptionDefinition name = rich.Name;
        CliParser parser = new();
        ParseResult first = parser.Parse(model, argv);
        string[] snapshot = [.. first.Arguments];
        string[] diagnostics = [.. first.Diagnostics.Select(static d => d.ToString(false))];
        ParseResult second = parser.Parse(model, argv);
        Check(first.Outcome == second.Outcome && first.DirectiveInputValid == second.DirectiveInputValid
            && diagnostics.SequenceEqual(second.Diagnostics.Select(static d => d.ToString(false)), StringComparer.Ordinal), "parser determinism");
        Check(first.Diagnostics.Count <= Limits.MaximumDiagnostics, "parser diagnostics");
        if (first.Outcome == ParseOutcome.Invalid)
        {
            Check(first.Values.Count == 0, "invalid parse exposed values");
            bool rejected = false;
            try
            {
                _ = first.TryGetOptionValue(name, out string? _);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Check(rejected, "invalid parse exposed typed value");
        }

        ParseResult french = new CliParser(numericCulture: System.Globalization.CultureInfo.GetCultureInfo("fr-FR")).Parse(model, argv);
        Check(french.Diagnostics.Count <= Limits.MaximumDiagnostics
            && (french.Outcome != ParseOutcome.Invalid || french.Values.Count == 0), "culture parser invariants");

        RichModel.Reset();
        using StringWriter output = new();
        using StringWriter errors = new();
        InvocationResult invoked = new CliInvoker().InvokeAsync(model, argv, new CliHostOptions(output, errors)).AsTask().GetAwaiter().GetResult();
        if (!first.DirectiveInputValid)
        {
            Check(first.Directives.Count == 0 && RichModel.Directives == 0 && RichModel.Commands == 0, "invalid directive dispatched");
        }

        if (invoked.Outcome is InvocationOutcome.InvalidInput or InvocationOutcome.HelpRequested or InvocationOutcome.HelpTreeRequested)
        {
            Check(RichModel.Commands == 0, "non-dispatchable parse dispatched command");
        }

        if (invoked.Outcome == InvocationOutcome.InvalidInput)
        {
            Check(output.ToString().Length == 0, "invalid input wrote standard output");
        }

        string scalar = input.Text();
        ParseResult valid = parser.Parse(model, ["--name=" + scalar]);
        Check(valid.Outcome == ParseOutcome.Validated || scalar.Length > 100, "argv string accepted");
        if (valid.Outcome == ParseOutcome.Validated)
        {
            Check(valid.TryGetOptionValue(name, out string? actual) && actual == scalar, "argv string preservation");
        }

        Check(first.Arguments.SequenceEqual(snapshot, StringComparer.Ordinal)
            && first.Diagnostics.Select(static d => d.ToString(false)).SequenceEqual(diagnostics, StringComparer.Ordinal), "prior result mutated");
    }

    private static void Check(bool condition, string invariant)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Fuzz invariant failed: " + invariant);
        }
    }

    private ref struct Input(ReadOnlySpan<byte> data)
    {
        // Selector bytes 192..255 pick one of these grammar tokens; lower values start length-prefixed text.
        // Keep exactly 64 entries and append only at the end so committed seeds stay meaningful.
        private static readonly string[] Palette =
        [
            "--count=", "--count", "-c", "--mode=", "--mode", "-m", "--ratio", "--ratio=",
            "--flag", "--no-flag", "-f", "-vvv", "-v", "-q", "--tag=", "--tag",
            "--files", "--label=", "--secret=", "--big=", "--level=", "--amount=", "run", "r",
            "deep", "list", "debug", "--target=", "-t", "--dry-run", "--", "-",
            "--help", "-h", "--help-tree", "[trace]", "[set:", "[level:", "[level]", "]",
            "[completion:query-v1]", "0", "1", "-1", "42", "101", "2147483648", "1.5",
            "1,5", "NaN", "abc", "read", "write", "off", "true", "false",
            "alpha", "al", "bad", "", "=", "-vq", "-c5", "--count=5"
        ];

        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;

        internal byte Byte() => _offset < _data.Length ? _data[_offset++] : (byte)0;

        internal string Token()
        {
            int selector = Byte();
            if (selector < 192)
            {
                _offset--;
                return Text();
            }

            string token = Palette[selector - 192];
            return token.EndsWith('=', StringComparison.Ordinal)
                ? token + Text()
                : token.EndsWith(':', StringComparison.Ordinal) ? token + Text() + "]" : token;
        }

        internal string Text()
        {
            int length = Math.Min(Byte() % 65, (_data.Length - _offset) / 2);
            Span<char> characters = stackalloc char[length];
            for (int i = 0; i < length; i++)
            {
                characters[i] = (char)(_data[_offset] | (_data[_offset + 1] << 8));
                _offset += 2;
            }

            return new string(characters);
        }
    }
}
