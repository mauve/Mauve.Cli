using System;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Declarations;

/// <summary>Chooses how command-line text is converted into a property value.</summary>
/// <typeparam name="TConverter">
/// A type implementing <c>ICliValueConverter&lt;T&gt;</c>, where <c>T</c> is the value
/// type to convert. The provider supplies a static <c>Convert</c> method; no instance is created.
/// </typeparam>
/// <remarks>
/// <para>
/// Put this attribute on a custom value type to use its converter wherever that type appears
/// in the generated model. Put it on a <c>[CliOption]</c> or <c>[CliArgument]</c> property
/// to change only that property's conversion. A property-level converter takes precedence
/// over a type-level converter. For a collection, it converts each item, not the whole list.
/// It can also be used on a standalone directive's value property.
/// </para>
/// <para>
/// Use a converter when text has a special format, such as a hexadecimal number or a build
/// identifier. Use validation attributes instead when the text already converts correctly
/// but the resulting value must satisfy a rule, such as a minimum value.
/// Built-in types do not need this attribute unless you want to replace their usual format.
/// </para>
/// <para>
/// The provider must be accessible to the generated code and match the target type exactly.
/// Return a failed conversion for bad input; unexpected exceptions propagate to the host.
/// The generator emits direct calls, so this does not require runtime reflection.
/// </para>
/// </remarks>
/// <example>
/// Accept <c>--mask FF</c> as the integer 255 without changing other integer options:
/// <code language="csharp"><![CDATA[
/// using System.Globalization;
/// using Mauve.Cli.Conversion;
/// using Mauve.Cli.Declarations;
///
/// [CliCommand("tool", IsRoot = true)]
/// public partial class ToolCommand
/// {
///     [CliOption(IsRequired = true)]
///     [CliConverter<HexIntConverter>(CompletionSafe = true)]
///     public int Mask { get; init; }
/// }
///
/// public readonly struct HexIntConverter : ICliValueConverter<int>
/// {
///     public static ValueConversion<int> Convert(string value, ConversionContext context)
///     {
///         return int.TryParse(value, NumberStyles.AllowHexSpecifier,
///             CultureInfo.InvariantCulture, out int number)
///             ? ValueConversion.FromValue(number)
///             : ValueConversion.Failure<int>("hex.invalid");
///     }
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property, Inherited = false)]
public sealed class CliConverterAttribute<TConverter> : Attribute
{
    /// <summary>Gets or sets whether completion is allowed to call this converter. The default is <see langword="false"/>.</summary>
    /// <remarks>
    /// Set <c>CompletionSafe = true</c> only for fast, side-effect-free converters that can
    /// safely run while the user is typing. Completion may receive incomplete input.
    /// Leaving this false does not disable normal parsing; it prevents completion from
    /// calling this provider. This setting does not register a completion-candidate provider.
    /// </remarks>
    public bool CompletionSafe { get; set; }
}

/// <summary>Turns a partial class into a command whose options, arguments and handler are generated at build time.</summary>
/// <param name="name">
/// The command word, without an executable path or parent command names.
/// Omit it to remove a trailing <c>Command</c> from the class name and convert the rest
/// to lowercase words separated by hyphens: <c>ListFilesCommand</c> becomes <c>list-files</c>.
/// </param>
/// <remarks>
/// <para>
/// Use a top-level, non-generic partial class. Mark the entry command with
/// <c>IsRoot = true</c>, then connect child commands with <c>[CliChild&lt;T&gt;]</c>
/// or <c>[CliParent&lt;T&gt;]</c>. Merely adding this attribute does not attach a
/// command to another command or run it.
/// </para>
/// <para>
/// Declare values as auto-properties with <c>get; init;</c> and mark them with
/// <c>[CliOption]</c> or <c>[CliArgument]</c>. Mark at most one instance method with
/// <c>[CliHandler]</c>. A command can have children without having its own handler.
/// Generated binding creates a fresh command object for each handler invocation;
/// parsing, help and completion do not create one.
/// </para>
/// <para>
/// The root provides generated <c>CreateModel()</c> and <c>CreateBuilder()</c> methods.
/// Use <c>CliInvoker.RunAsync&lt;TRoot&gt;</c> to run it, or use the model directly
/// for parsing. Choose the builder API instead when you need to assemble a model in code
/// rather than from attributes.
/// </para>
/// </remarks>
/// <example>
/// This command accepts <c>greet --name Ada</c>; omitting the option uses <c>friend</c>.
/// The host passes only <c>--name Ada</c> as arguments when this is the root command.
/// <code language="csharp"><![CDATA[
/// using System;
/// using Mauve.Cli.Declarations;
///
/// [CliCommand("greet", IsRoot = true, Aliases = new[] { "hello" },
///     Description = "Greet someone.",
///     LongDescription = "Print a greeting using the supplied name.")]
/// public partial class GreetCommand
/// {
///     [CliOption]
///     public string Name { get; init; } = "friend";
///
///     [CliHandler]
///     private void Run() => Console.WriteLine($"Hello, {Name}!");
/// }
///
/// // In your entry point:
/// // return await Mauve.Cli.Invocation.CliInvoker.RunAsync<GreetCommand>(args);
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliCommandAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the supplied command word, or <see langword="null"/> when it will be inferred from the class name.</summary>
    /// <remarks>
    /// For example, <c>[CliCommand("list")]</c> uses <c>list</c> even if the class is
    /// named <c>ListFilesCommand</c>. A child name is a single word in the command path,
    /// not a combined string such as <c>"tool list"</c>. The root name is used in help
    /// and default environment-variable names; users do not repeat it in the argv passed to the parser.
    /// </remarks>
    public string? Name { get; } = name;

    /// <summary>Gets or sets whether this command is an entry point for a generated command tree. The default is <see langword="false"/>.</summary>
    /// <remarks>
    /// Set this to true on the command passed to <c>CliInvoker.RunAsync&lt;T&gt;</c>.
    /// Leave it false on children. A root cannot also be another command's child.
    /// A compilation may contain separate roots for separate command trees.
    /// </remarks>
    public bool IsRoot { get; set; }

    /// <summary>Gets or sets alternative command words, in declaration order. The default is an empty array.</summary>
    /// <remarks>
    /// For example, <c>Aliases = new[] { "ls", "dir" }</c> lets a child named
    /// <c>list</c> be selected with any of those three words. Aliases do not create
    /// separate handlers or change the canonical name. Names and aliases must not
    /// collide with another command under the same parent.
    /// </remarks>
    public string[] Aliases { get; set; } = [];

    /// <summary>Gets or sets the short explanation shown for this command in help.</summary>
    /// <remarks>
    /// If omitted, the generator uses the class's XML <c>summary</c>.
    /// Supplying this property explicitly overrides that summary, even with
    /// <see langword="null"/> or an empty string. Use a short sentence here;
    /// put detailed guidance in <see cref="LongDescription"/> or the class's XML <c>remarks</c>.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>Gets or sets the longer explanation shown in this command's detailed help.</summary>
    /// <remarks>
    /// If omitted, the generator uses the class's XML <c>remarks</c>.
    /// An explicitly supplied value, including null or an empty string, overrides those
    /// remarks. Use this for behavior, limitations and guidance that would make
    /// <see cref="Description"/> too long.
    /// </remarks>
    public string? LongDescription { get; set; }

    /// <summary>Gets or sets whether this command is omitted from ordinary help and completion listings. The default is <see langword="false"/>.</summary>
    /// <remarks>
    /// A hidden command can still be selected by name or alias. Hiding is useful for
    /// internal or compatibility commands, but it is not an access-control mechanism.
    /// </remarks>
    public bool IsHidden { get; set; }
}

/// <summary>Declares an action that can run before normal command processing, using bracket syntax such as <c>[notice]</c>.</summary>
/// <param name="name">
/// The directive name, without brackets or a colon. A method directive must supply it.
/// For a standalone class, omit it to remove a trailing <c>Directive</c> and convert
/// the rest of the class name to hyphen-separated lowercase words.
/// </param>
/// <remarks>
/// <para>
/// Put this attribute on an instance method of the root command for a directive without
/// declared values. Alternatively, put it on a standalone partial class, declare its
/// values as <c>[CliOption]</c> partial get-only properties, and add one
/// <c>[CliHandler]</c>. Include that class on the root with <c>[CliDirective&lt;T&gt;]</c>.
/// A standalone directive needs a parameterless constructor.
/// </para>
/// <para>
/// Users place directives before the first child command, positional argument or
/// <c>--</c>. A directive with values uses <c>[name:value]</c> or a bracket span such
/// as <c>[name: first second]</c>. Values bind in property declaration order;
/// they are not named options inside the brackets.
/// </para>
/// <para>
/// All directive input is checked before any directive runs. Valid directives run in
/// command-line order, before ordinary help or input-error handling and before the command
/// handler. Return <c>DirectiveResult.ContinueProcessing</c> to continue or
/// <c>DirectiveResult.StopProcessing(code)</c> to stop, even with exit code zero.
/// Returning integer zero continues; a nonzero integer stops. A <c>void</c> handler
/// continues. The same rules apply to supported task-returning handlers.
/// </para>
/// <para>
/// Use a normal option for a value the command handler needs. Use a child command for a
/// separate user operation. Use a directive for a pre-processing action that may need
/// to run even when ordinary command input is invalid. A root with method directives
/// needs a parameterless constructor and cannot have C# required members.
/// </para>
/// </remarks>
/// <example>
/// A method directive needs no separate registration. Running <c>tool [notice]</c>
/// prints the notice before the normal command outcome:
/// <code language="csharp"><![CDATA[
/// using System;
/// using Mauve.Cli.Declarations;
/// using Mauve.Cli.Invocation;
///
/// [CliCommand("tool", IsRoot = true)]
/// public partial class ToolCommand
/// {
///     [CliDirective("notice", Description = "Print a notice first.")]
///     private DirectiveResult Notice()
///     {
///         Console.WriteLine("Notice directive ran.");
///         return DirectiveResult.ContinueProcessing;
///     }
///
///     [CliHandler]
///     private int Run() => 0;
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class CliDirectiveAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the supplied directive name, or <see langword="null"/> to infer it from a standalone class name.</summary>
    /// <remarks>
    /// For example, <c>AuditDirective</c> becomes <c>audit</c>, used as <c>[audit]</c>.
    /// A method must use an explicit name such as <c>[CliDirective("audit")]</c>;
    /// the method name is not used as a fallback.
    /// </remarks>
    public string? Name { get; } = name;

    /// <summary>Gets or sets the explanation displayed for the directive in help.</summary>
    /// <remarks>
    /// Describe the action and whether it can stop command processing, for example
    /// <c>Description = "Print version information and exit."</c>.
    /// The default is null. This text does not change execution or stop behavior.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>Gets or sets whether help and completion omit this directive. The default is <see langword="false"/>.</summary>
    /// <remarks>A hidden directive can still run when explicitly requested. Hiding does not restrict access.</remarks>
    public bool IsHidden { get; set; }
}

/// <summary>Makes a standalone directive available to a root command and all of its children.</summary>
/// <typeparam name="TDirective">A partial class marked with <c>[CliDirective]</c> in the same compilation.</typeparam>
/// <remarks>
/// <para>
/// Place this attribute on the root, not on a child. Repeat it with different types
/// to include several directives. Each registered directive must have a unique name.
/// Declaring a standalone directive does not register it automatically.
/// </para>
/// <para>
/// Use this form when a directive has values or is easier to maintain in its own class.
/// For a small, value-free action, placing <c>[CliDirective("name")]</c> directly
/// on a root method avoids the separate class and inclusion attribute.
/// </para>
/// </remarks>
/// <example>
/// Running <c>tool [audit:release]</c> binds <c>release</c> to <c>Label</c>:
/// <code language="csharp"><![CDATA[
/// using System;
/// using Mauve.Cli.Declarations;
///
/// [CliCommand("tool", IsRoot = true)]
/// [CliDirective<AuditDirective>]
/// public partial class ToolCommand
/// {
///     [CliHandler]
///     private int Run() => 0;
/// }
///
/// [CliDirective("audit", Description = "Print an audit label first.")]
/// public partial class AuditDirective
/// {
///     [CliOption(IsRequired = true)]
///     public partial string Label { get; }
///
///     [CliHandler]
///     private void Run() => Console.WriteLine($"Audit: {Label}");
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliDirectiveAttribute<TDirective> : Attribute
    where TDirective : class
{
    /// <summary>Gets the standalone directive type named in the attribute's type argument.</summary>
    /// <remarks>This identifies the declaration; reading it does not construct or invoke the directive.</remarks>
    public Type DirectiveType => typeof(TDirective);
}

/// <summary>Adds another command below the command carrying this attribute.</summary>
/// <typeparam name="TCommand">A class marked with <c>[CliCommand]</c> in the same compilation.</typeparam>
/// <remarks>
/// <para>
/// Place one attribute per child on the parent. The child's own name and aliases
/// determine the words users type after the parent command.
/// Sibling order follows source-file order and attribute position in the compilation.
/// Both declarations must be partial classes; the child must not be a root.
/// </para>
/// <para>
/// Alternatively, place <c>[CliParent&lt;TParent&gt;]</c> on the child.
/// Choose the form that keeps registration easiest to find; do not declare the same
/// parent-child relationship with both forms. Command hierarchies cannot contain cycles.
/// Options on a parent are not automatically available on children; use
/// <c>Scope = OptionScope.Inherited</c> for that.
/// </para>
/// </remarks>
/// <example>
/// Users select the child with <c>tool list</c>:
/// <code language="csharp"><![CDATA[
/// using Mauve.Cli.Declarations;
///
/// [CliCommand("tool", IsRoot = true)]
/// [CliChild<ListCommand>]
/// public partial class ToolCommand { }
///
/// [CliCommand("list", Description = "List available items.")]
/// public partial class ListCommand
/// {
///     [CliHandler]
///     private int Run() => 0;
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliChildAttribute<TCommand> : Attribute
    where TCommand : class
{
    /// <summary>Gets the child command type named in the attribute's type argument.</summary>
    /// <remarks>The referenced declaration supplies its name, aliases, values and handler; this attribute only connects it to the parent.</remarks>
    public Type CommandType => typeof(TCommand);
}

/// <summary>Attaches the command carrying this attribute below another command.</summary>
/// <typeparam name="TCommand">The parent class, marked with <c>[CliCommand]</c> in the same compilation.</typeparam>
/// <remarks>
/// <para>
/// Place this attribute on the child when you want the child to declare where it belongs.
/// The parent does not also need <c>[CliChild&lt;TChild&gt;]</c>. Declaring both for the
/// same relationship is an error. A root cannot have a parent, and cycles are rejected.
/// Repeat the attribute with different parents to make a declaration reachable under
/// different command paths, provided each resulting tree has no name collisions.
/// </para>
/// <para>
/// This connects CLI commands; it is not C# class inheritance and does not copy properties
/// or handlers from the parent. Use an option pack to reuse a set of option properties.
/// </para>
/// </remarks>
/// <example>
/// This is the child-owned alternative to putting <c>[CliChild&lt;ListCommand&gt;]</c> on the root:
/// <code language="csharp"><![CDATA[
/// using Mauve.Cli.Declarations;
///
/// [CliCommand("tool", IsRoot = true)]
/// public partial class ToolCommand { }
///
/// [CliCommand("list")]
/// [CliParent<ToolCommand>]
/// public partial class ListCommand
/// {
///     [CliHandler]
///     private int Run() => 0;
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliParentAttribute<TCommand> : Attribute
    where TCommand : class
{
    /// <summary>Gets the parent command type named in the attribute's type argument.</summary>
    /// <remarks>The current declaration becomes a child of this type; it does not become a C# subclass.</remarks>
    public Type CommandType => typeof(TCommand);
}

/// <summary>Binds a named option, such as <c>--output file.txt</c>, to a property.</summary>
/// <param name="name">
/// The long option name, normally written without <c>--</c>.
/// Omit it to infer a hyphen-separated lowercase name from the property, unless
/// <see cref="ShortName"/> is set, in which case omitting the name creates a short-only option.
/// </param>
/// <remarks>
/// <para>
/// On a command or option pack, use an auto-property with <c>get; init;</c>.
/// The property type usually chooses the syntax: <c>bool</c> is a flag,
/// <c>IReadOnlyList&lt;T&gt;</c> consumes a collection, and other types consume one value.
/// The number of values consumed by one occurrence is separate from whether the option
/// must occur at all: use <see cref="IsRequired"/> for required presence.
/// </para>
/// <para>
/// An optional scalar needs a nullable type or a supported constant initializer.
/// For example, <c>public int Count { get; init; } = 10;</c> uses 10 when absent,
/// whereas <c>public int? Count { get; init; }</c> can remain unavailable.
/// A nullable type does not by itself allow the option to be used without a value.
/// Flag, counting and collection properties must not declare initializers.
/// </para>
/// <para>
/// Use <c>[CliArgument]</c> instead for a value identified by position rather than a name.
/// Add validation attributes for constraints on converted values, or
/// <c>[CliConverter&lt;T&gt;]</c> for a custom text format.
/// </para>
/// <para>
/// In a standalone directive, this attribute has a different binding role: mark a
/// <c>partial</c>, get-only property to receive one of the directive's ordered bare values.
/// Users supply <c>[audit:label]</c>, not <c>[audit:--label value]</c>.
/// </para>
/// </remarks>
/// <example>
/// These declarations accept <c>--verbose --output result.txt --tag red --tag blue</c>:
/// <code language="csharp"><![CDATA[
/// using System.Collections.Generic;
/// using Mauve.Cli.Declarations;
/// using Mauve.Cli.Definitions;
///
/// [CliCommand("tool", IsRoot = true)]
/// public partial class ToolCommand
/// {
///     [CliOption("verbose", ShortName = 'v', Description = "Show progress.")]
///     public bool Verbose { get; init; }
///
///     [CliOption("output", ShortName = 'o', ValueLabel = "FILE")]
///     public string Output { get; init; } = "output.txt";
///
///     [CliOption("tag", Arity = OptionArity.RepeatedSingleCollection)]
///     public required IReadOnlyList<string> Tags { get; init; }
///
///     [CliOption(Arity = OptionArity.OptionalScalar, BareValuePolicy = BareValuePolicy.Null)]
///     public string? Color { get; init; }
/// }
/// ]]></code>
/// Here <c>--color=always</c> supplies text, <c>--color</c> supplies null, and
/// <c>--color always</c> leaves <c>always</c> to be parsed separately.
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CliOptionAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the supplied long name, or <see langword="null"/> for an inferred or short-only name.</summary>
    /// <remarks>
    /// <c>[CliOption]</c> on <c>OutputFile</c> creates <c>--output-file</c>.
    /// <c>[CliOption("out")]</c> creates <c>--out</c>.
    /// <c>[CliOption(ShortName = 'o')]</c> creates only <c>-o</c>; supply both
    /// a name and a short name to expose both spellings. The generated definitions store
    /// names without prefixes, allowing host syntax settings to choose the prefixes.
    /// </remarks>
    public string? Name { get; } = name;

    /// <summary>Gets or sets how an occurrence consumes values. Omit it to infer the behavior from the property type.</summary>
    /// <remarks>
    /// <para>The available choices are:</para>
    /// <list type="bullet">
    /// <item><description><c>RequiredScalar</c>: consume one value, as in <c>--count 3</c> or <c>--count=3</c>. This does not make the option required.</description></item>
    /// <item><description><c>OptionalScalar</c>: consume only an inline value, as in <c>--color=auto</c>. A bare occurrence needs <c>BareValuePolicy.Null</c> and a nullable property.</description></item>
    /// <item><description><c>BooleanFlag</c>: a <c>bool</c> switch, such as <c>--verbose</c>, which is false when absent and true when supplied bare. This is inferred for <c>bool</c>, not <c>bool?</c>.</description></item>
    /// <item><description><c>RepeatedSingleCollection</c>: each occurrence adds one item, as in <c>--tag red --tag blue</c>.</description></item>
    /// <item><description><c>GreedyCollection</c>: each occurrence takes one or more adjacent values, as in <c>--tag red blue</c>; later occurrences append more items. This is inferred for <c>IReadOnlyList&lt;T&gt;</c>.</description></item>
    /// <item><description><c>Counting</c>: each valueless occurrence increments an <c>int</c> or <c>int?</c>, as in <c>-v -v</c>. The count starts at zero.</description></item>
    /// </list>
    /// <para>
    /// Collection modes require <c>IReadOnlyList&lt;T&gt;</c>. Other types infer
    /// <c>RequiredScalar</c>. Command-line collection items are separate values:
    /// <c>--tag red,blue</c> supplies one string, not two automatically split strings.
    /// </para>
    /// </remarks>
    public OptionArity Arity { get; set; }

    /// <summary>Gets or sets alternative long names without option prefixes. The default is an empty array.</summary>
    /// <remarks>
    /// For example, <c>[CliOption("output", Aliases = new[] { "out" })]</c>
    /// accepts both <c>--output</c> and <c>--out</c> for the same property.
    /// Use <see cref="ShortName"/> or <see cref="ShortAliases"/> for single-dash
    /// spellings such as <c>-o</c>; a one-letter long alias is still a long option.
    /// All spellings must be unique within the commands where the option is visible.
    /// </remarks>
    public string[] Aliases { get; set; } = [];

    /// <summary>Gets or sets the main single-letter short name. The default null character means no short name.</summary>
    /// <remarks>
    /// Use an ASCII letter without a prefix, for example <c>ShortName = 'v'</c>
    /// for <c>-v</c>. With no explicit long name, this makes the option short-only.
    /// Use <c>[CliOption("verbose", ShortName = 'v')]</c> to expose both
    /// <c>--verbose</c> and <c>-v</c>. Short names are case-sensitive.
    /// </remarks>
    public char ShortName { get; set; }

    /// <summary>Gets or sets additional single-letter short names. The default is an empty array.</summary>
    /// <remarks>
    /// For example, <c>ShortAliases = new[] { 'V' }</c> can add <c>-V</c>
    /// alongside <c>ShortName = 'v'</c>. Each entry must be an ASCII letter, with
    /// no duplicates or repetition of the primary short name.
    /// </remarks>
    public char[] ShortAliases { get; set; } = [];

    /// <summary>Gets or sets whether the user must supply this option, overriding inferred requiredness when explicitly set.</summary>
    /// <remarks>
    /// <para>
    /// If omitted, requiredness comes from the C# <c>required</c> modifier or
    /// <c>System.ComponentModel.DataAnnotations.RequiredAttribute</c>; otherwise the
    /// option is optional. <c>IsRequired = false</c> explicitly overrides those markers.
    /// </para>
    /// <para>
    /// For example, <c>[CliOption(IsRequired = true)] public int Count { get; init; }</c>
    /// requires an occurrence but still accepts zero. Use validation to reject particular
    /// values. A property initializer cannot satisfy required presence; an enabled, valid
    /// environment binding can. Do not confuse this with <c>Arity = RequiredScalar</c>,
    /// which only says a present option needs a value.
    /// </para>
    /// </remarks>
    public bool IsRequired { get; set; }

    /// <summary>Gets or sets whether the bound value is allowed to be null.</summary>
    /// <remarks>
    /// The generator already allows null for nullable scalar or element types.
    /// Setting this to true still requires a compatible nullable type; it does not turn
    /// an <c>int</c> property into <c>int?</c>. Setting false does not override
    /// nullable-type inference. This controls value validity, not required presence or
    /// token consumption; use <see cref="IsRequired"/> or <see cref="Arity"/> for those.
    /// </remarks>
    public bool AllowsNull { get; set; }

    /// <summary>Gets or sets whether help and completion omit this option. The default is <see langword="false"/>.</summary>
    /// <remarks>
    /// The option remains accepted when supplied. Hidden names use a generic label in
    /// default diagnostics. This is a discoverability setting, not access control;
    /// use <see cref="IsSensitive"/> to mark values that must not be advertised.
    /// </remarks>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets whether values should be treated as sensitive in help, completion and validation diagnostics.</summary>
    /// <remarks>
    /// The default is false. Use <c>IsSensitive = true</c> for passwords or tokens:
    /// default and allowed-value help text, value suggestions and public validation reasons
    /// are suppressed. The option name can still appear in help; use <see cref="IsHidden"/>
    /// to hide the option itself. This does not encrypt values, hide argv from the operating
    /// system or redact your own output. Parsed values remain available to application code.
    /// </remarks>
    public bool IsSensitive { get; set; }

    /// <summary>Gets or sets the explanation shown for this option in help.</summary>
    /// <remarks>
    /// If omitted, the property's XML <c>summary</c> supplies the text.
    /// Explicit null or empty text overrides that summary. Describe what the option does,
    /// for example <c>Description = "Write the report to this file."</c>;
    /// use <see cref="ValueLabel"/> for the short value placeholder.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>Gets or sets the placeholder used for the option's value in help.</summary>
    /// <remarks>
    /// For example, <c>ValueLabel = "FILE"</c> makes help describe an output value
    /// as <c>FILE</c>. The default null lets help use its normal placeholder.
    /// This does not rename the property, change conversion or restrict accepted values.
    /// </remarks>
    public string? ValueLabel { get; set; }

    /// <summary>Gets or sets whether this option is available only on its command or also on descendant commands.</summary>
    /// <remarks>
    /// The default, <c>OptionScope.Local</c>, makes the option valid only when its
    /// owner is the final selected command. <c>OptionScope.Inherited</c> makes it valid
    /// on the owner and descendants, before or after child-command words once the owner
    /// is selected. This also applies to options declared in packs.
    /// Inherited values are not copied onto child properties; use the invocation context
    /// to read an ancestor's value. A local parent option does not remain valid merely
    /// because it appeared before a child name.
    /// </remarks>
    /// <example>
    /// On a root with child <c>list</c>, this allows both <c>tool --verbose list</c>
    /// and <c>tool list --verbose</c>:
    /// <code language="csharp">
    /// [CliOption(Scope = OptionScope.Inherited)]
    /// public bool Verbose { get; init; }
    /// </code>
    /// </example>
    public OptionScope Scope { get; set; }

    /// <summary>Gets or sets what happens when an optional-valued option is present without an inline value.</summary>
    /// <remarks>
    /// <para>
    /// For declarative <c>OptionArity.OptionalScalar</c>, explicitly select
    /// <c>BareValuePolicy.Null</c> and use a nullable property. The bare option then
    /// supplies null; an absent option still follows its separate absent/default behavior.
    /// </para>
    /// <para>
    /// Leave this at <c>Unspecified</c> for other arities. The enum also contains
    /// <c>Constant</c> and <c>Factory</c>, but the attribute does not supply their
    /// values or callbacks; use explicit option definitions for those policies.
    /// A property initializer supplies an absent default, not a bare-option value.
    /// </para>
    /// </remarks>
    public BareValuePolicy BareValuePolicy { get; set; }

    /// <summary>Gets or sets whether this option may receive its value from an environment variable.</summary>
    /// <remarks>
    /// <para>
    /// <c>Inherit</c>, the default, follows the host's default eligibility policy.
    /// <c>Enabled</c> opts this option in and <c>Disabled</c> opts it out.
    /// All choices still require the host to enable the feature with
    /// <c>new CliHostOptions(enableEnvironmentVariables: true)</c>.
    /// The attribute alone never turns environment lookup on.
    /// </para>
    /// <para>
    /// Explicit command-line input wins over environment input, which wins over a
    /// declared default. Valid environment input can satisfy requiredness and runs through
    /// normal conversion and validation. Environment binding is part of invocation;
    /// directly parsing argv with <c>CliParser.Parse</c> does not read the environment.
    /// Environment collection values use CSV, unlike separate argv collection tokens.
    /// </para>
    /// </remarks>
    public EnvironmentVariableBinding EnvironmentVariableBinding { get; set; }

    /// <summary>Gets or sets the complete environment-variable name to use instead of an inferred name.</summary>
    /// <remarks>
    /// When null, the name comes from the host prefix (by default the root command name)
    /// and canonical long option name, uppercased with hyphens replaced by underscores.
    /// For root <c>my-tool</c> and option <c>output-file</c>, that is
    /// <c>MY_TOOL_OUTPUT_FILE</c>. An explicit name is used as-is, with no prefix.
    /// Short-only options need an explicit name. Setting a name does not enable binding;
    /// <see cref="EnvironmentVariableBinding"/> and the host policy still apply.
    /// </remarks>
    /// <example>
    /// With environment binding enabled by the host, read <c>SERVICE_TOKEN</c> when
    /// the option is absent from argv:
    /// <code language="csharp">
    /// [CliOption(EnvironmentVariableBinding = EnvironmentVariableBinding.Enabled,
    ///     EnvironmentVariableName = "SERVICE_TOKEN", IsSensitive = true)]
    /// public string? Token { get; init; }
    /// </code>
    /// </example>
    public string? EnvironmentVariableName { get; set; }
}

/// <summary>Binds an unnamed command-line value to a property according to its position.</summary>
/// <param name="name">
/// The argument's display name, not a word users must type. Omit it to infer
/// hyphen-separated lowercase words from the property name, such as <c>InputFile</c> to <c>input-file</c>.
/// </param>
/// <remarks>
/// <para>
/// Put this attribute on a command's auto-property with <c>get; init;</c>.
/// Arguments bind in declaration order. A scalar accepts one value and is required
/// by default. An <c>IReadOnlyList&lt;T&gt;</c> property is a rest argument:
/// it consumes remaining positional values and requires at least one by default.
/// Put a rest argument last; required arguments must not follow optional ones.
/// </para>
/// <para>
/// Set <c>MinimumValues = 0</c> for an optional argument. An optional scalar needs
/// a nullable type or a supported constant default. A nullable type alone does not make
/// the argument optional. Collection initializers are not supported.
/// </para>
/// <para>
/// Use <c>[CliOption]</c> instead when users should identify a value by name.
/// Add validation attributes to restrict converted values, and use <c>--</c> in argv
/// when positional text could otherwise be read as an option or command.
/// </para>
/// </remarks>
/// <example>
/// This accepts <c>copy source.txt</c> or <c>copy source.txt destination.txt</c>:
/// <code language="csharp"><![CDATA[
/// using Mauve.Cli.Declarations;
///
/// [CliCommand("copy", IsRoot = true)]
/// public partial class CopyCommand
/// {
///     [CliArgument(Description = "File to read.")]
///     public required string Source { get; init; }
///
///     [CliArgument(MinimumValues = 0, Description = "Optional destination.")]
///     public string? Destination { get; init; }
/// }
/// ]]></code>
/// For many input files instead of one, use a final collection property:
/// <code language="csharp"><![CDATA[
/// [CliArgument(MinimumValues = 0, MaximumValues = -1)]
/// public required System.Collections.Generic.IReadOnlyList<string> Files { get; init; }
/// ]]></code>
/// This collection can be empty because its explicit minimum is zero. Here the C#
/// <c>required</c> modifier tells C# callers to initialize the property; generated
/// binding does that even when the CLI supplies no files.
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CliArgumentAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the supplied display name, or <see langword="null"/> to infer it from the property name.</summary>
    /// <remarks>
    /// For example, <c>[CliArgument("source")]</c> names the value in help and diagnostics.
    /// Users type the actual value, such as <c>report.txt</c>, not <c>source=report.txt</c>.
    /// Changing the name does not change the argument's declaration-order position.
    /// </remarks>
    public string? Name { get; } = name;

    /// <summary>Gets or sets whether the argument needs a value: 1 means required and 0 means optional.</summary>
    /// <remarks>
    /// The default is 1 for both scalar and rest arguments. The declarative API supports
    /// only 0 or 1 here. Set <c>MinimumValues = 0</c> explicitly for optional input,
    /// even with a nullable property or C# <c>required</c> member.
    /// For a rest argument needing a larger collection count, use a length validation attribute.
    /// </remarks>
    public int MinimumValues { get; set; } = 1;

    /// <summary>Gets or sets whether to accept one value (1) or all remaining positional values (-1).</summary>
    /// <remarks>
    /// If omitted, a scalar property uses 1 and an <c>IReadOnlyList&lt;T&gt;</c>
    /// property uses -1. Explicit 1 requires a scalar; -1 requires a collection.
    /// Other maximums are not supported by the declarative API. Use a collection length
    /// validator to cap the accepted item count rather than setting this to, for example, 5.
    /// </remarks>
    public int MaximumValues { get; set; } = 1;

    /// <summary>Gets or sets whether a converted value is allowed to be null.</summary>
    /// <remarks>
    /// Nullable scalar and element types already enable null values in the generated model.
    /// Setting true requires a compatible nullable type; setting false does not undo
    /// nullable-type inference. This does not make an argument optional:
    /// set <c>MinimumValues = 0</c> to allow it to be absent.
    /// </remarks>
    public bool AllowsNull { get; set; }

    /// <summary>Gets or sets whether help and completion omit this argument. The default is <see langword="false"/>.</summary>
    /// <remarks>
    /// The argument is still parsed in its normal position and still follows its
    /// requiredness rules. Default diagnostics replace its name with a generic label.
    /// Hiding does not remove a required argument or restrict who can supply it.
    /// </remarks>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets whether this argument's values are sensitive. The default is <see langword="false"/>.</summary>
    /// <remarks>
    /// Set true to suppress default and allowed-value help text, value suggestions and
    /// public validation reasons. The argument name can remain visible; use
    /// <see cref="IsHidden"/> to hide it. This does not encrypt or erase parsed values,
    /// hide argv from the operating system, or protect values printed by your own code.
    /// </remarks>
    public bool IsSensitive { get; set; }

    /// <summary>Gets or sets the explanation shown for this argument in help.</summary>
    /// <remarks>
    /// If omitted, the property's XML <c>summary</c> supplies the text.
    /// An explicitly supplied null or empty string overrides that summary.
    /// Explain what users should provide, for example <c>Description = "Path to the input file."</c>;
    /// this text does not validate the path or change conversion.
    /// </remarks>
    public string? Description { get; set; }
}

/// <summary>Selects the instance method to run after the command's input has been converted and validated.</summary>
/// <remarks>
/// <para>
/// A command can have at most one marked method. It may be private, but must not be
/// static or generic. Use no parameter, one <c>CancellationToken</c>, or one
/// <c>CommandInvocationContext</c>. Read ordinary values from the initialized command
/// properties; use the context for details such as whether a value was explicitly supplied.
/// </para>
/// <para>
/// Supported command return types are <c>void</c>, <c>int</c>, <c>Task</c>,
/// <c>Task&lt;int&gt;</c>, <c>ValueTask</c> and <c>ValueTask&lt;int&gt;</c>.
/// An integer becomes the exit code; no integer means success with code zero.
/// Do not use <c>async void</c>. Use <c>CliInvoker.RunAsync&lt;TRoot&gt;</c> for
/// asynchronous handlers. The synchronous <c>Run&lt;TRoot&gt;</c> entry point is
/// available only when the entire reachable command tree has synchronous handlers.
/// </para>
/// <para>
/// A command without a handler can still group children or provide help, but invoking it
/// as the final command produces the host's no-handler outcome. Use validators rather
/// than handler checks for invalid input that should be diagnosed before invocation.
/// Unexpected handler exceptions propagate to the host.
/// </para>
/// <para>
/// A standalone <c>[CliDirective]</c> class requires exactly one marked handler.
/// Directive handlers additionally support <c>DirectiveResult</c>,
/// <c>Task&lt;DirectiveResult&gt;</c> and <c>ValueTask&lt;DirectiveResult&gt;</c>
/// to choose whether normal processing continues. A method already marked
/// <c>[CliDirective("name")]</c> must not also be marked <c>[CliHandler]</c>.
/// </para>
/// </remarks>
/// <example>
/// A cancellation-aware asynchronous command:
/// <code language="csharp"><![CDATA[
/// using System;
/// using System.Threading;
/// using System.Threading.Tasks;
/// using Mauve.Cli.Declarations;
///
/// [CliCommand("work", IsRoot = true)]
/// public partial class WorkCommand
/// {
///     [CliOption]
///     public int Delay { get; init; } = 100;
///
///     [CliHandler]
///     private async Task<int> RunAsync(CancellationToken cancellationToken)
///     {
///         await Task.Delay(Delay, cancellationToken);
///         Console.WriteLine("Finished.");
///         return 0;
///     }
/// }
/// ]]></code>
/// For a synchronous action, use <c>[CliHandler] private int Run() =&gt; 0;</c>
/// instead; an asynchronous method is not required.
/// </example>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CliHandlerAttribute : Attribute
{
}

/// <summary>Groups reusable option properties into a partial class that commands receive through their constructors.</summary>
/// <remarks>
/// <para>
/// Declare the pack's options with <c>[CliOption]</c> on <c>get; init;</c>
/// auto-properties. Include the pack by adding a parameter of its type to a command's
/// constructor. A pack can depend on other packs in the same way. Each command or pack
/// needs exactly one instance constructor, and its parameters must be declared pack types;
/// these constructors are not general-purpose dependency injection.
/// </para>
/// <para>
/// The generator creates fresh pack objects for each invocation. Nested packs have their
/// values before they are passed to their parent's constructor. A pack's own properties
/// are initialized after its constructor returns, so do not read them in that constructor.
/// A parent cannot request the same direct pack type twice; dependency cycles are rejected.
/// </para>
/// <para>
/// Packs reuse declarations and can group options in help; they do not add a command word
/// or option prefix. For example, a <c>NetworkOptions</c> pack's <c>Host</c> property
/// normally becomes <c>--host</c>, not <c>--network-options-host</c>.
/// Options are local unless they explicitly set <c>Scope = OptionScope.Inherited</c>.
/// Use inherited scope for one ancestor-owned option shared with descendants, or include
/// a pack in separate commands when each command should own its own options.
/// </para>
/// </remarks>
/// <example>
/// Both the command and its handler can use the typed pack:
/// <code language="csharp"><![CDATA[
/// using System;
/// using Mauve.Cli.Declarations;
///
/// [CliOptionPack(Title = "Network", Description = "Connection settings.")]
/// public partial class NetworkOptions
/// {
///     [CliOption]
///     public string Host { get; init; } = "localhost";
///
///     [CliOption]
///     public int Port { get; init; } = 8080;
/// }
///
/// [CliCommand("connect", IsRoot = true)]
/// public partial class ConnectCommand(NetworkOptions network)
/// {
///     [CliHandler]
///     private void Run() => Console.WriteLine($"{network.Host}:{network.Port}");
/// }
/// ]]></code>
/// Users can supply <c>connect --host example.org --port 443</c>.
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliOptionPackAttribute : Attribute
{
    /// <summary>Gets or sets the heading under which this pack's options appear in help.</summary>
    /// <remarks>
    /// If omitted, the pack class's XML <c>summary</c> supplies the title.
    /// Explicit null or empty text overrides that summary. A nested pack without its
    /// own title uses the nearest titled parent pack's group; otherwise normal option
    /// grouping applies. This title does not change option names or parser behavior.
    /// </remarks>
    public string? Title { get; set; }

    /// <summary>Gets or sets the longer explanation for this pack's help group.</summary>
    /// <remarks>
    /// If omitted, the pack class's XML <c>remarks</c> supplies the description.
    /// An explicitly supplied value, including null or an empty string, overrides those
    /// remarks. Use this to explain how the grouped options work together; it does not
    /// add validation or affect whether options are required.
    /// </remarks>
    public string? Description { get; set; }
}
