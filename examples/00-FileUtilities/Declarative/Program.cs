using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Conversion;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace FileUtilities.Declarative;

/// <summary>Copy, inspect and compare files.</summary>
/// <remarks>
/// The buffer size defaults to 64KiB, can be set with <c>--buffer-size</c> or
/// <c>FILEUTILS_BUFFER_SIZE</c>, and accepts byte, KiB and MiB values from
/// 1KiB through 1MiB. A command-line value takes precedence over the environment;
/// the environment takes precedence over the default.
/// </remarks>
/// <example><code>$0 copy source.txt destination.txt</code></example>
/// <example><code>$0 inspect source.txt</code></example>
/// <example><code>$0 compare left.txt right.txt</code></example>
[CliCommand("fileutils", IsRoot = true, Aliases = ["fileutils-declarative"], Description = "Copy, inspect and compare files.")]
[CliChild<CopyCommand>]
[CliChild<InspectCommand>]
[CliChild<CompareCommand>]
[CliChild<SettingsCommand>]
[CliDirective<AboutDirective>]
internal sealed partial class FileUtilitiesCommand
{
}

[CliDirective(Description = "Show information about fileutils.")]
internal sealed partial class AboutDirective
{
    [CliHandler]
    internal DirectiveResult Run(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("fileutils: copy, inspect and compare files.");
        return DirectiveResult.StopProcessing(0);
    }
}

/// <summary>Copy a file to a destination.</summary>
/// <remarks>Copy is the default action; use <c>--dry-run</c> to preview without changes.</remarks>
/// <example><code>$0 source.txt destination.txt</code></example>
/// <example><code>$0 source.txt destination.txt --overwrite</code></example>
/// <example><code>$0 source.txt destination.txt --dry-run</code></example>
[CliCommand("copy", Aliases = ["cp"], Description = "Copy a file.")]
internal sealed partial class CopyCommand(BufferOptions buffer)
{
    public BufferOptions Buffer { get; } = buffer;

    /// <summary>The source file to copy.</summary>
    [CliArgument(Description = "Source file path.")]
    public required string Source { get; init; }

    /// <summary>The destination file path.</summary>
    [CliArgument(Description = "Destination file path.")]
    public required string Destination { get; init; }

    /// <summary>Replace an existing destination.</summary>
    [CliOption("overwrite", ShortName = 'f', Description = "Replace an existing destination.")]
    public bool Overwrite { get; init; }

    /// <summary>Preview the copy without changing files.</summary>
    [CliOption("dry-run", ShortName = 'n', Description = "Preview the copy without changing files.")]
    public bool DryRun { get; init; }

    [CliHandler]
    internal async Task<int> Run(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using FileStream source = Program.OpenRead(Source, Buffer.BufferSize);
        if (DryRun)
        {
            if (!Overwrite && (File.Exists(Destination) || Directory.Exists(Destination)))
            {
                throw new IOException("Destination exists.");
            }

            await Console.Out.WriteLineAsync($"Would copy: {Source} -> {Destination}".AsMemory(), cancellationToken).ConfigureAwait(false);
            return 0;
        }

        Program.RejectSameFile(Source, Destination);
        await Program.CopyToDestinationAsync(
            source, Destination, Buffer.BufferSize, Overwrite, cancellationToken).ConfigureAwait(false);

        await Console.Out.WriteLineAsync($"Copied: {Source} -> {Destination}".AsMemory(), cancellationToken).ConfigureAwait(false);
        return 0;
    }
}

/// <summary>Print a file path and its byte length.</summary>
/// <example><code>$0 source.txt</code></example>
[CliCommand("inspect", Aliases = ["stat"], Description = "Show a file's path and length.")]
internal sealed partial class InspectCommand
{
    /// <summary>The file to inspect.</summary>
    [CliArgument(Description = "File path.")]
    public required string Path { get; init; }

    [CliHandler]
    internal int Run(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using FileStream file = Program.OpenRead(Path, 4096);
        Console.WriteLine($"Path: {Path}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Length: {file.Length}"));
        return 0;
    }
}

/// <summary>Compare two files for byte-for-byte equality.</summary>
/// <example><code>$0 left.txt right.txt</code></example>
[CliCommand("compare", Aliases = ["cmp"], Description = "Compare two files.")]
internal sealed partial class CompareCommand(BufferOptions buffer)
{
    public BufferOptions Buffer { get; } = buffer;

    /// <summary>The left file to compare.</summary>
    [CliArgument(Description = "Left file path.")]
    public required string Left { get; init; }

    /// <summary>The right file to compare.</summary>
    [CliArgument(Description = "Right file path.")]
    public required string Right { get; init; }

    [CliHandler]
    internal async Task<int> Run(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using FileStream left = Program.OpenRead(Left, Buffer.BufferSize);
        using FileStream right = Program.OpenRead(Right, Buffer.BufferSize);
        byte[] leftBuffer = new byte[Buffer.BufferSize];
        byte[] rightBuffer = new byte[Buffer.BufferSize];

        while (true)
        {
            int leftCount = await Program.ReadChunkAsync(left, leftBuffer, cancellationToken).ConfigureAwait(false);
            int rightCount = await Program.ReadChunkAsync(right, rightBuffer, cancellationToken).ConfigureAwait(false);
            if (leftCount != rightCount
                || !leftBuffer.AsSpan(0, leftCount).SequenceEqual(rightBuffer.AsSpan(0, rightCount)))
            {
                await Console.Out.WriteLineAsync("Different".AsMemory(), cancellationToken).ConfigureAwait(false);
                return 1;
            }

            if (leftCount == 0)
            {
                await Console.Out.WriteLineAsync("Equal".AsMemory(), cancellationToken).ConfigureAwait(false);
                return 0;
            }
        }
    }
}

/// <summary>Show the configured buffer size and its input origin.</summary>
/// <example><code>$0 --buffer-size 64KiB</code></example>
[CliCommand("settings", Description = "Show the buffer-size option value origin without its value.")]
internal sealed partial class SettingsCommand(BufferOptions buffer)
{
    public BufferOptions Buffer { get; } = buffer;

    [CliHandler]
    internal int Run(CommandInvocationContext context)
    {
        ParsedValue parsedValue = context.ParseResult.Values.Single(static value =>
            value.Option is OptionDefinition option && option.Name == "--buffer-size");
        Console.WriteLine($"--buffer-size: {parsedValue.Origin}");
        return 0;
    }
}

/// <summary>Reusable bounded buffer-size settings.</summary>
[CliOptionPack(Title = "Buffer")]
internal sealed partial class BufferOptions
{
    /// <summary>Buffer size in bytes; defaults to 64KiB and accepts 1KiB through 1MiB.</summary>
    [CliOption(
        "buffer-size",
        Description = "Buffer size in bytes, KiB or MiB (1KiB to 1MiB). Command line overrides FILEUTILS_BUFFER_SIZE, then the default.",
        EnvironmentVariableBinding = EnvironmentVariableBinding.Enabled,
        EnvironmentVariableName = "FILEUTILS_BUFFER_SIZE")]
    [CliConverter<ByteSizeConverter>(CompletionSafe = true)]
    public int BufferSize { get; init; } = 64 * 1024;
}

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
    private static readonly CompletionProviderRegistry _completionProviders = CreateCompletionProviders();

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

        CliHostOptions options = new CliHostOptionsBuilder()
            .WithEnvironmentVariables(enabled: true, prefix: "FILEUTILS", enabledByDefault: false)
            .MapException<FileNotFoundException>(66, "The requested file was not found.")
            .MapException<DirectoryNotFoundException>(66, "The requested file was not found.")
            .MapException<UnauthorizedAccessException>(77, "Access to the requested file was denied.")
            .MapException<IOException>(74, "The file operation failed.")
            .WithExceptionRenderer(static context => "fileutils: " + context.Result.SafeMessage)
            .Build();

        try
        {
            CommandModel model = CreateModel();
            return args is ["--demo-completion"]
                ? await ShowCompletionAsync(model, cancellation.Token).ConfigureAwait(false)
                : await CliInvoker.DispatchMulticallAsync(
                    [model],
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
        CommandBuilder builder = FileUtilitiesCommand.CreateBuilder()
            .AddDirective(PredefinedDirectives.CreateCompletion("fileutils", providers: _completionProviders))
            .AddDirective(PredefinedDirectives.CreateDebug());
        return CommandModel.Create(builder, includeStandardHelp: true);
    }

    private static CompletionProviderRegistry CreateCompletionProviders()
    {
        CommandModel model = FileUtilitiesCommand.CreateModel();
        OptionDefinition option = model.Root.Children
            .SelectMany(static command => command.Options)
            .First(static candidate => candidate.Name == "--buffer-size");
        return new CompletionProviderRegistryBuilder()
            .Register(option, GetBufferSizes)
            .Build();
    }

    private static async Task<int> ShowCompletionAsync(CommandModel model, CancellationToken cancellationToken)
    {
        CompletionRequest request = new(model, ["copy", "--buffer-size="], 1, "--buffer-size=".Length);
        CompletionResult result = await new CompletionService()
            .CompleteAsync(request, _completionProviders, cancellationToken)
            .ConfigureAwait(false);
        if (result.Outcome != CompletionOutcome.Completed || !result.IsComplete)
        {
            throw new InvalidOperationException("Buffer-size completion did not complete successfully.");
        }

        foreach (CompletionCandidate candidate in result.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"Buffer size: {candidate.InsertionText}");
        }

        return 0;
    }

    private static async IAsyncEnumerable<CompletionValue> GetBufferSizes(
        CompletionContext _,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new CompletionValue("4KiB", description: "4 kibibytes.");
        cancellationToken.ThrowIfCancellationRequested();
        yield return new CompletionValue("64KiB", description: "64 kibibytes.");
        cancellationToken.ThrowIfCancellationRequested();
        yield return new CompletionValue("1MiB", description: "1 mebibyte.");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    internal static FileStream OpenRead(string path, int bufferSize)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    internal static async ValueTask<int> ReadChunkAsync(
        FileStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    internal static async Task CopyToDestinationAsync(
        FileStream source,
        string destination,
        int bufferSize,
        bool overwrite,
        CancellationToken cancellationToken)
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
                await source.CopyToAsync(output, bufferSize, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
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

    internal static void RejectSameFile(string source, string destination)
    {
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (comparer.Equals(Path.GetFullPath(source), Path.GetFullPath(destination)))
        {
            throw new IOException("Source and destination are the same file.");
        }
    }
}
