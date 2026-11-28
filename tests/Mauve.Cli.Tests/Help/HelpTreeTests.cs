using System;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Help;

[TestClass]
public sealed class HelpTreeTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-006")]
    public async Task RunAsync_HelpTreeDoesNotInvokeApplicationCallbacks()
    {
        int converterCalls = 0;
        int factoryCalls = 0;
        int validatorCalls = 0;
        int crossValidatorCalls = 0;
        int handlerCalls = 0;
        ConverterRegistry converters = new ConverterRegistryBuilder().Register((_, _) =>
        {
            converterCalls++;
            return ValueConversion.Failure<int>("integer.invalid");
        }).Build();
        ValueValidator<int> validator = new((_, _) =>
        {
            validatorCalls++;
            return ValueValidationOutcome.Success;
        });
        CommandBuilder child = new CommandBuilder("child")
            .AddOption(new OptionDefinition("count", typeof(int), OptionArity.RequiredScalar,
                validators: [validator]))
            .AddOption(new OptionDefinition("label", typeof(string), OptionArity.RequiredScalar,
                defaultValuePolicy: DefaultValuePolicy.Factory,
                defaultValueFactory: () =>
                {
                    factoryCalls++;
                    return "default";
                }))
            .AddCrossValidator(_ =>
            {
                crossValidatorCalls++;
                return ValueValidationOutcome.Success;
            })
            .SetHandler((_, _) => handlerCalls++);
        CommandModel model = CommandModel.Create(
            new CommandBuilder("tool").AddChild(child), includeStandardHelp: true);
        using StringWriter output = new();
        using StringWriter errors = new();
        CliHostOptions options = new(output, errors, converters: converters);

        int exitCode = await CliInvoker.RunAsync(model, ["child", "--count=bad", "--help-tree"],
            options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("tool\n├── ... other commands omitted...\n└── child" + Environment.NewLine, output.ToString());
        Assert.AreEqual(string.Empty, errors.ToString());
        Assert.AreEqual(0, converterCalls);
        Assert.AreEqual(0, factoryCalls);
        Assert.AreEqual(0, validatorCalls);
        Assert.AreEqual(0, crossValidatorCalls);
        Assert.AreEqual(0, handlerCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-006")]
    public void Render_ProjectsVisibleFullAndSelectedTreesSafely()
    {
        CommandBuilder hidden = new("hidden") { IsHidden = true };
        _ = hidden.AddChild(new CommandBuilder("secret"));
        CommandBuilder group = new CommandBuilder("group") { Description = "Group\u001b\u202e\ncommands" }
            .AddChild(new CommandBuilder("first")
                .AddChild(new CommandBuilder("nested")))
            .AddChild(new CommandBuilder("second") { Description = "Second" })
            .AddChild(hidden);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool") { Description = "Root" }
            .AddChild(group)
            .AddChild(new CommandBuilder("other")));

        HelpTreeDocument full = HelpTreeDocument.Create(model);
        HelpTreeDocument selected = HelpTreeDocument.Create(model, model.Root.Children[0].Children[0]);

        Assert.AreEqual(
            "tool (Root)\n├── group (Group commands)\n│   ├── first\n│   │   └── nested\n│   └── second (Second)\n└── other",
            HelpTreeRenderer.Render(full));
        Assert.AreEqual(
            "tool (Root)\n├── ... other commands omitted...\n└── first\n    └── nested",
            HelpTreeRenderer.Render(selected));
        Assert.IsTrue(selected.Items[1].IsOmission);
        Assert.DoesNotContain(static item => item.Name is "hidden" or "secret", full.Items);
        Assert.ThrowsExactly<ArgumentException>(() => HelpTreeDocument.Create(model, model.Root.Children[0].Children[2]));
        Assert.ThrowsExactly<ArgumentException>(() => HelpTreeDocument.Create(model, model.Root.Children[0].Children[2].Children[0]));
        Assert.ThrowsExactly<ArgumentException>(() => HelpTreeDocument.Create(model, CommandModel.Create(new CommandBuilder("other")).Root));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-006")]
    public void Write_UsesSanitizedProjectionAndPropagatesOutputFaults()
    {
        HelpTreeDocument document = HelpTreeDocument.Create(CommandModel.Create(
            new CommandBuilder("tool") { Description = "safe\u001btext" }));
        string? output = null;
        HelpTreeOutput.Write(document, text => output = text, static tree => tree.Items[0].Description);
        Assert.AreEqual("safetext", output);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            HelpTreeOutput.Write(document, static _ => throw new InvalidOperationException("sink fault")));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            HelpTreeOutput.Write(document, static _ => { }, static _ => null!));
    }
}
