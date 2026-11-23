using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Validation.Declarative;

[CliCommand("validate", IsRoot = true, Description = "Demonstrate declarative validation.")]
internal sealed partial class ValidateCommand
{
    [CliOption]
    [CliRange(1, 50)]
    public int Limit { get; init; } = 10;

    [CliOption]
    [CliMinLength(2)]
    [CliMaxLength(20)]
    [CliNotEmpty]
    public string Name { get; init; } = "guest";

    [CliOption]
    [CliAllowedValues("safe", "fast")]
    public string Mode { get; init; } = "safe";

    [CliOption]
    [CliValidator<LabelValidator>]
    public string Label { get; init; } = "ready";

    [CliOption]
    [CliMinValue("2026-01-01")]
    [CliMaxValue("2026-12-31")]
    public DateOnly? Start { get; init; }

    [CliOption("tag", Arity = OptionArity.RepeatedSingleCollection, IsRequired = true)]
    [CliLength(1, 4)]
    [CliNotEmpty]
    [CliLength(2, 12, Target = CliValidationTarget.Items)]
    [CliValidator<TagValidator>(Target = CliValidationTarget.Items)]
    public required IReadOnlyList<string> Tags { get; init; }

    [CliArgument(MinimumValues = 0)]
    [CliNotEmpty]
    public string? Path { get; init; }

    [CliCrossValidator]
    private static ValueValidationOutcome CheckMode(int limit, string mode) =>
        mode == "fast" && limit > 20
            ? ValueValidationOutcome.FailureForParameter("mode.limit", 1, message: "Fast mode requires --limit to be at most 20.")
            : ValueValidationOutcome.Success;

    [CliHandler]
    internal int Run()
    {
        string start = Start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(none)";
        Console.WriteLine($"limit={Limit}; name={Name}; mode={Mode}; label={Label}; tags={string.Join(",", Tags)}; path={Path ?? "(none)"}; start={start}");
        return 0;
    }
}

internal sealed class LabelValidator : ICliValidator<string>
{
    public static ValueValidationOutcome Validate(string? value, ValidationContext context) =>
        value == "blocked" ? ValueValidationOutcome.Failure("label.blocked", "This label is reserved; choose another label.") : ValueValidationOutcome.Success;
}

internal sealed class TagValidator : ICliValidator<string>
{
    public static ValueValidationOutcome Validate(string? value, ValidationContext context) =>
        value?.StartsWith('x', StringComparison.Ordinal) == true
            ? throw new CliValidationException(ValueValidationOutcome.FailureAt("tag.reserved", 1, "Tags starting with 'x' are reserved."))
            : ValueValidationOutcome.Success;
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        CommandModel model = ValidateCommand.CreateModel(includeStandardHelp: true);
        if (args is ["--demo-completion"])
        {
            CompletionResult result = await new CompletionService().CompleteAsync(
                new CompletionRequest(model, ["--mode=f"], 0, "--mode=f".Length)).ConfigureAwait(false);
            foreach (CompletionCandidate candidate in result.Candidates)
            {
                Console.WriteLine(candidate.InsertionText);
            }

            return result.IsComplete ? 0 : 1;
        }

        return await CliInvoker.RunAsync(model, args).ConfigureAwait(false);
    }
}
