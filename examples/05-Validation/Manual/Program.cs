using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Validation.Manual;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        OptionDefinition limit = new("limit", typeof(int), OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: 10, defaultDisplay: "10",
            validators: [CliValidators.Range(1, 50)]);
        OptionDefinition name = new("name", typeof(string), OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: "guest", defaultDisplay: "guest",
            validators: [CliValidators.StringLength(2, int.MaxValue), CliValidators.StringLength(0, 20), CliValidators.NotEmpty()]);
        OptionDefinition mode = new("mode", typeof(string), OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: "safe", defaultDisplay: "safe",
            validators: [CliValidators.AllowedValues("safe", "fast")]);
        OptionDefinition label = new("label", typeof(string), OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: "ready", defaultDisplay: "ready",
            validators: [new ValueValidator<string>(static (value, _) => value == "blocked"
                ? ValueValidationOutcome.Failure("label.blocked", "This label is reserved; choose another label.") : ValueValidationOutcome.Success)]);
        OptionDefinition start = new("start", typeof(DateOnly?), OptionArity.RequiredScalar, allowsNull: true,
            validators:
            [
                CliValidators.Nullable(CliValidators.Minimum(new DateOnly(2026, 1, 1))),
                CliValidators.Nullable(CliValidators.Maximum(new DateOnly(2026, 12, 31)))
            ]);
        OptionDefinition tags = new("tag", typeof(IReadOnlyList<string>), OptionArity.RepeatedSingleCollection,
            elementType: typeof(string), isRequired: true,
            validators:
            [
                CliValidators.CollectionLength<string>(1, 4),
                CliValidators.NotEmptyCollection<string>(),
                CliValidators.Each<string>(CliValidators.StringLength(2, 12)),
                CliValidators.Each<string>(new ValueValidator<string>(static (value, _) =>
                    value?.StartsWith('x', StringComparison.Ordinal) == true
                        ? throw new CliValidationException(ValueValidationOutcome.FailureAt("tag.reserved", 1, "Tags starting with 'x' are reserved."))
                        : ValueValidationOutcome.Success))
            ]);
        PositionalDefinition path = new("path", typeof(string), minimumValues: 0, allowsNull: true,
            validators: [CliValidators.NotEmpty()]);
        CommandModel model = CommandModel.Create(new CommandBuilder("validate")
            .AddOption(limit).AddOption(name).AddOption(mode).AddOption(label).AddOption(start).AddOption(tags).AddPositional(path)
            .AddCrossValidator(context =>
                context.TryGetOptionValue(limit, out int limitValue)
                && context.TryGetOptionValue(mode, out string? modeValue)
                && modeValue == "fast" && limitValue > 20
                    ? ValueValidationOutcome.Failure("mode.limit", mode, message: "Fast mode requires --limit to be at most 20.")
                    : ValueValidationOutcome.Success)
            .SetHandler((context, cancellationToken) =>
            {
                _ = context.TryGetOptionValue(limit, out int limitValue);
                _ = context.TryGetOptionValue(name, out string? nameValue);
                _ = context.TryGetOptionValue(mode, out string? modeValue);
                _ = context.TryGetOptionValue(label, out string? labelValue);
                _ = context.TryGetOptionValue(start, out DateOnly? startValue);
                _ = context.TryGetOptionValue(tags, out IReadOnlyList<string>? tagValues);
                _ = context.TryGetPositionalValue(path, out string? pathValue);
                string startText = startValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(none)";
                Console.WriteLine($"limit={limitValue}; name={nameValue}; mode={modeValue}; label={labelValue}; tags={string.Join(",", tagValues!)}; path={pathValue ?? "(none)"}; start={startText}");
                return 0;
            }), includeStandardHelp: true);
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
