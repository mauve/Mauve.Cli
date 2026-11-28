using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace FileUtilities.Manual;

internal readonly struct ByteSizeConverter : ICliValueConverter<int>
{
    public static ValueConversion<int> Convert(string value, ConversionContext context)
    {
        ReadOnlySpan<char> text = value.AsSpan();
        int multiplier = 1;
        if (text.EndsWith("KiB", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024;
            text = text[..^3];
        }
        else if (text.EndsWith("MiB", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024 * 1024;
            text = text[..^3];
        }
        else if (text.EndsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^1];
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number >= 0
            && (long)number * multiplier is >= 1024 and <= 1024 * 1024
                ? ValueConversion.FromValue(number * multiplier)
                : ValueConversion.Failure<int>("byte-size.invalid");
    }
}

internal static class Program
{
    private static readonly OptionDefinition _bufferSize = new(
        "buffer-size", typeof(int), OptionArity.RequiredScalar,
        description: "Buffer size in bytes, KiB or MiB (1KiB to 1MiB). Command line overrides FILEUTILS_BUFFER_SIZE, then the default.",
        defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: 65536,
        defaultDisplay: "65536", converter: ValueConverterRegistration.Create<int, ByteSizeConverter>(completionSafe: true),
        environmentVariableBinding: EnvironmentVariableBinding.Enabled,
        environmentVariableName: "FILEUTILS_BUFFER_SIZE");
    private static readonly OptionPackBuilder _bufferPack = new OptionPackBuilder().AddOption(_bufferSize);
    private static readonly CompletionProviderRegistry _completionProviders = new CompletionProviderRegistryBuilder()
        .Register(_bufferSize, CompleteBufferSizes)
        .Build();
    private static readonly OptionDefinition _dryRun = new(
        "dry-run", typeof(bool), OptionArity.BooleanFlag, shortName: 'n', description: "Preview without changing files.");
    private static readonly OptionDefinition _overwrite = new(
        "overwrite", typeof(bool), OptionArity.BooleanFlag, shortName: 'f', description: "Replace an existing destination.");
    private static readonly PositionalDefinition _source = new("source", typeof(string), description: "Source file.");
    private static readonly PositionalDefinition _destination = new("destination", typeof(string), description: "Destination file.");
    private static readonly PositionalDefinition _path = new("path", typeof(string), description: "File to inspect.");
    private static readonly PositionalDefinition _left = new("left", typeof(string), description: "First file.");
    private static readonly PositionalDefinition _right = new("right", typeof(string), description: "Second file.");

    private static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancellation = new();
        void CancelOnInterrupt(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }

        ConsoleCancelEventHandler cancelHandler = CancelOnInterrupt;
        Console.CancelKeyPress += cancelHandler;
        try
        {
            CommandModel model = CreateModel();
            if (args is ["--demo-completion"])
            {
                return await ShowCompletionAsync(model, cancellation.Token).ConfigureAwait(false);
            }

            CliHostOptions options = new CliHostOptionsBuilder()
                .WithEnvironmentVariables(enabledByDefault: false)
                .MapException<FileNotFoundException>(66, "The requested file was not found.")
                .MapException<DirectoryNotFoundException>(66, "The requested file was not found.")
                .MapException<UnauthorizedAccessException>(77, "Access to the requested file was denied.")
                .MapException<IOException>(74, "The file operation failed.")
                .WithExceptionRenderer(static context => "fileutils: " + context.Result.SafeMessage)
                .Build();
            return await CliInvoker.DispatchMulticallAsync(
                [model],
                routes:
                [
                    new CliMulticallRoute("mcopy", ["fileutils", "copy"]),
                    new CliMulticallRoute("minspect", ["fileutils", "inspect"]),
                    new CliMulticallRoute("mcompare", ["fileutils", "compare"])
                ],
                arguments: args,
                options: options,
                cancellationToken: cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static CommandModel CreateModel()
    {
        DirectiveBuilder about = new DirectiveBuilder("about")
        {
            Description = "Show information about fileutils."
        }.SetHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            Console.WriteLine("fileutils: copy, inspect and compare files.");
            return DirectiveResult.StopProcessing(0);
        });

        return CommandModel.Create(new CommandBuilder("fileutils")
        {
            Description = "Copy, inspect and compare files."
        }
            .AddAlias("fileutils-manual")
            .AddDirective(about)
            .AddDirective(PredefinedDirectives.CreateCompletion("fileutils", providers: _completionProviders))
            .AddDirective(PredefinedDirectives.CreateDebug())
            .AddExample("$0 copy source.txt destination.txt")
            .AddExample("$0 inspect source.txt")
            .AddExample("$0 compare left.txt right.txt")
            .AddChild(new CommandBuilder("copy") { Description = "Copy a file." }
                .AddAlias("cp")
                .AddPositional(_source).AddPositional(_destination)
                .AddOption(_dryRun).AddOption(_overwrite)
                .AddPack("buffer", _bufferPack, "Buffer")
                .AddExample("$0 source.txt destination.txt --dry-run")
                .SetTaskHandlerWithExitCode(CopyAsync))
            .AddChild(new CommandBuilder("inspect") { Description = "Show a file's path and length." }
                .AddAlias("stat")
                .AddPositional(_path)
                .AddExample("$0 source.txt")
                .SetHandler(Inspect))
            .AddChild(new CommandBuilder("compare") { Description = "Compare two files." }
                .AddAlias("cmp")
                .AddPositional(_left).AddPositional(_right)
                .AddPack("buffer", _bufferPack, "Buffer")
                .AddExample("$0 left.txt right.txt")
                .SetTaskHandlerWithExitCode(CompareAsync))
            .AddChild(new CommandBuilder("settings") { Description = "Show the buffer-size option value origin without its value." }
                .AddPack("buffer", _bufferPack, "Buffer")
                .AddExample("$0 --buffer-size 64KiB")
                .SetHandler((context, _) => ShowSettings(context))),
            includeStandardHelp: true);
    }

    private static async Task<int> CopyAsync(CommandInvocationContext context, CancellationToken token)
    {
        string source = ReadPath(context, _source);
        string destination = ReadPath(context, _destination);
        int bufferSize = ReadBuffer(context);
        bool dryRun = ReadOption<bool>(context, _dryRun);
        bool overwrite = ReadOption<bool>(context, _overwrite);
        token.ThrowIfCancellationRequested();
        using FileStream input = OpenRead(source, bufferSize);
        if (dryRun)
        {
            if (!overwrite && (File.Exists(destination) || Directory.Exists(destination)))
            {
                throw new IOException("Destination exists.");
            }

            Console.WriteLine($"Would copy: {source} -> {destination}");
            return 0;
        }

        RejectSameFile(source, destination);
        await CopyToDestinationAsync(input, destination, bufferSize, overwrite, token).ConfigureAwait(false);
        Console.WriteLine($"Copied: {source} -> {destination}");
        return 0;
    }

    private static int Inspect(CommandInvocationContext context, CancellationToken token)
    {
        string path = ReadPath(context, _path);
        token.ThrowIfCancellationRequested();
        using FileStream input = OpenRead(path, 4096);
        Console.WriteLine($"Path: {path}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Length: {input.Length}"));
        return 0;
    }

    private static async Task<int> CompareAsync(CommandInvocationContext context, CancellationToken token)
    {
        int bufferSize = ReadBuffer(context);
        token.ThrowIfCancellationRequested();
        using FileStream left = OpenRead(ReadPath(context, _left), bufferSize);
        using FileStream right = OpenRead(ReadPath(context, _right), bufferSize);
        byte[] leftBuffer = new byte[bufferSize];
        byte[] rightBuffer = new byte[bufferSize];
        while (true)
        {
            int leftCount = await left.ReadAtLeastAsync(leftBuffer, bufferSize, throwOnEndOfStream: false, token).ConfigureAwait(false);
            int rightCount = await right.ReadAtLeastAsync(rightBuffer, bufferSize, throwOnEndOfStream: false, token).ConfigureAwait(false);
            if (leftCount != rightCount || !leftBuffer.AsSpan(0, leftCount).SequenceEqual(rightBuffer.AsSpan(0, rightCount)))
            {
                Console.WriteLine("Different");
                return 1;
            }

            if (leftCount == 0)
            {
                Console.WriteLine("Equal");
                return 0;
            }
        }
    }

    private static FileStream OpenRead(string path, int bufferSize)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task CopyToDestinationAsync(
        FileStream source,
        string destination,
        int bufferSize,
        bool overwrite,
        CancellationToken token)
    {
        string fullDestination = Path.GetFullPath(destination);
        if (!overwrite && (File.Exists(fullDestination) || Directory.Exists(fullDestination)))
        {
            throw new IOException("Destination exists.");
        }

        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(fullDestination) ?? Directory.GetCurrentDirectory(),
            $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");
        bool moved = false;
        try
        {
            FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (output.ConfigureAwait(false))
            {
                await source.CopyToAsync(output, bufferSize, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullDestination, overwrite);
            moved = true;
        }
        finally
        {
            if (!moved && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void RejectSameFile(string source, string destination)
    {
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (comparer.Equals(Path.GetFullPath(source), Path.GetFullPath(destination)))
        {
            throw new IOException("Source and destination are the same file.");
        }
    }

    private static async IAsyncEnumerable<CompletionValue> CompleteBufferSizes(
        CompletionContext context,
        [EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        yield return new CompletionValue("4KiB");
        token.ThrowIfCancellationRequested();
        yield return new CompletionValue("64KiB");
        token.ThrowIfCancellationRequested();
        yield return new CompletionValue("1MiB");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async Task<int> ShowCompletionAsync(CommandModel model, CancellationToken cancellationToken)
    {
        CompletionResult result = await new CompletionService().CompleteAsync(
            new CompletionRequest(model, ["copy", "--buffer-size="], 1, "--buffer-size=".Length),
            _completionProviders,
            cancellationToken).ConfigureAwait(false);
        if (result.Outcome != CompletionOutcome.Completed || !result.IsComplete)
        {
            throw new InvalidOperationException("Buffer size completion did not complete successfully.");
        }

        foreach (CompletionCandidate candidate in result.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"Buffer size: {candidate.InsertionText}");
        }

        return 0;
    }

    private static int ReadBuffer(CommandInvocationContext context)
        => context.GetOptionPack("buffer").TryGetOptionValue(_bufferSize, out int size)
            ? size : throw new InvalidOperationException("The buffer size is unavailable.");

    private static string ReadPath(CommandInvocationContext context, PositionalDefinition definition)
        => context.TryGetPositionalValue(definition, out string? value) && value is not null
            ? value : throw new InvalidOperationException("The file path is unavailable.");

    private static T ReadOption<T>(CommandInvocationContext context, OptionDefinition definition)
        => context.TryGetOptionValue(definition, out T? value) && value is not null
            ? value : throw new InvalidOperationException("The option is unavailable.");

    private static int ShowSettings(CommandInvocationContext context)
    {
        foreach (ParsedValue parsedValue in context.ParseResult.Values)
        {
            if (parsedValue.Option == _bufferSize)
            {
                Console.WriteLine($"--buffer-size: {parsedValue.Origin}");
            }
        }

        return 0;
    }
}
