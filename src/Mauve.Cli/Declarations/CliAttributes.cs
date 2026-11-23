using System;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Declarations;

/// <summary>Declares a static converter provider for a value type or bound property.</summary>
/// <typeparam name="TConverter">The provider implementing ICliValueConverter for the target type.</typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property, Inherited = false)]
public sealed class CliConverterAttribute<TConverter> : Attribute
{
    /// <summary>Gets or sets whether completion may invoke this provider.</summary>
    public bool CompletionSafe { get; set; }
}

/// <summary>Declares a command type used by the source-generated CLI model.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliCommandAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the explicit command name, or <see langword="null"/> to infer it.</summary>
    public string? Name { get; } = name;

    /// <summary>Gets or sets whether this declaration is a generated model root.</summary>
    public bool IsRoot { get; set; }

    /// <summary>Gets or sets ordered command aliases.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>Gets or sets the short description, overriding the command's XML summary when explicitly supplied.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the long description, overriding the command's XML remarks when explicitly supplied.</summary>
    public string? LongDescription { get; set; }

    /// <summary>Gets or sets whether ordinary help and completion hide the command.</summary>
    public bool IsHidden { get; set; }
}

/// <summary>Declares a standalone directive or a method directive on a command root.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class CliDirectiveAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the explicit directive name, or null to infer it from a type name.</summary>
    public string? Name { get; } = name;

    /// <summary>Gets or sets the display description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets whether help and completion hide this directive.</summary>
    public bool IsHidden { get; set; }
}

/// <summary>Includes a standalone directive in a generated command root.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliDirectiveAttribute<TDirective> : Attribute
    where TDirective : class
{
    /// <summary>Gets the included directive declaration type.</summary>
    public Type DirectiveType => typeof(TDirective);
}

/// <summary>Declares an ordered child command type.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliChildAttribute<TCommand> : Attribute
    where TCommand : class
{
    /// <summary>Gets the child command declaration type.</summary>
    public Type CommandType => typeof(TCommand);
}

/// <summary>Declares a parent command type for this command.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliParentAttribute<TCommand> : Attribute
    where TCommand : class
{
    /// <summary>Gets the parent command declaration type.</summary>
    public Type CommandType => typeof(TCommand);
}

/// <summary>Declares an option on a generated command or option-pack type.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CliOptionAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the prefix-free long option name, or <see langword="null"/> to infer it.</summary>
    public string? Name { get; } = name;

    /// <summary>Gets or sets an explicit option arity override.</summary>
    public OptionArity Arity { get; set; }

    /// <summary>Gets or sets prefix-free long option aliases.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>Gets or sets the primary short option character; the default null character means none.</summary>
    public char ShortName { get; set; }

    /// <summary>Gets or sets additional short option characters.</summary>
    public char[] ShortAliases { get; set; } = [];

    /// <summary>Gets or sets whether the option is required, overriding inferred requiredness when supplied.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Gets or sets whether null values are allowed.</summary>
    public bool AllowsNull { get; set; }

    /// <summary>Gets or sets whether the option is hidden from help and completion.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets whether values are sensitive.</summary>
    public bool IsSensitive { get; set; }

    /// <summary>Gets or sets the option description, overriding the property's XML summary when explicitly supplied.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the display label for a value.</summary>
    public string? ValueLabel { get; set; }

    /// <summary>Gets or sets the inherited visibility scope.</summary>
    public OptionScope Scope { get; set; }

    /// <summary>Gets or sets the behavior for an optional-valued option used without a value.</summary>
    public BareValuePolicy BareValuePolicy { get; set; }
}

/// <summary>Declares a positional argument on a generated command type.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CliArgumentAttribute(string? name = null) : Attribute
{
    /// <summary>Gets the explicit argument name, or <see langword="null"/> to infer it.</summary>
    public string? Name { get; } = name;

    /// <summary>Gets or sets the minimum number of values accepted, overriding inferred cardinality when supplied.</summary>
    public int MinimumValues { get; set; } = 1;

    /// <summary>Gets or sets the maximum number of values accepted, or -1 for a rest argument.</summary>
    public int MaximumValues { get; set; } = 1;

    /// <summary>Gets or sets whether null values are allowed.</summary>
    public bool AllowsNull { get; set; }

    /// <summary>Gets or sets whether the argument is hidden from help and completion.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets whether values are sensitive.</summary>
    public bool IsSensitive { get; set; }

    /// <summary>Gets or sets the argument description, overriding the property's XML summary when explicitly supplied.</summary>
    public string? Description { get; set; }
}

/// <summary>Marks the single instance method invoked by a generated command.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CliHandlerAttribute : Attribute
{
}

/// <summary>Marks a declaration type as a reusable generated option pack.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliOptionPackAttribute : Attribute
{
    /// <summary>Gets or sets the help-group title, overriding the pack's XML summary when explicitly supplied.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the help-group description, overriding the pack's XML remarks when explicitly supplied.</summary>
    public string? Description { get; set; }
}
