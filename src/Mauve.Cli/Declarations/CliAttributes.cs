using System;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Declarations;

/// <summary>Declares a command type used by the source-generated CLI model.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CliCommandAttribute(string name) : Attribute
{
    /// <summary>Gets the canonical command name or root display label.</summary>
    public string Name { get; } = name;

    /// <summary>Gets or sets whether this declaration is a generated model root.</summary>
    public bool IsRoot { get; set; }

    /// <summary>Gets or sets ordered command aliases.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>Gets or sets the short description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the long description.</summary>
    public string LongDescription { get; set; } = string.Empty;

    /// <summary>Gets or sets whether ordinary help and completion hide the command.</summary>
    public bool IsHidden { get; set; }
}

/// <summary>Declares an ordered child command type.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliChildAttribute(Type commandType, int order) : Attribute
{
    /// <summary>Gets the child command declaration type.</summary>
    public Type CommandType { get; } = commandType;

    /// <summary>Gets the explicit child order.</summary>
    public int Order { get; } = order;
}

/// <summary>Declares an option on a generated command type.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CliOptionAttribute(string name, OptionArity arity, int order) : Attribute
{
    /// <summary>Gets the canonical option spelling.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the option arity.</summary>
    public OptionArity Arity { get; } = arity;

    /// <summary>Gets the explicit declaration order.</summary>
    public int Order { get; } = order;

    /// <summary>Gets or sets option aliases.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>Gets or sets whether the option is required.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Gets or sets whether null values are allowed.</summary>
    public bool AllowsNull { get; set; }

    /// <summary>Gets or sets whether the option is hidden from help and completion.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets whether values are sensitive.</summary>
    public bool IsSensitive { get; set; }

    /// <summary>Gets or sets the option description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the display label for a value.</summary>
    public string ValueLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the inherited visibility scope.</summary>
    public OptionScope Scope { get; set; }

    /// <summary>Gets or sets the behavior for an optional-valued option used without a value.</summary>
    public BareValuePolicy BareValuePolicy { get; set; }
}

/// <summary>Declares a positional argument on a generated command type.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class CliArgumentAttribute(string name, int order) : Attribute
{
    /// <summary>Gets the diagnostic name of the positional argument.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the explicit declaration order.</summary>
    public int Order { get; } = order;

    /// <summary>Gets or sets the minimum number of values accepted.</summary>
    public int MinimumValues { get; set; } = 1;

    /// <summary>Gets or sets the maximum number of values accepted, or -1 for a rest argument.</summary>
    public int MaximumValues { get; set; } = 1;

    /// <summary>Gets or sets whether null values are allowed.</summary>
    public bool AllowsNull { get; set; }

    /// <summary>Gets or sets whether the argument is hidden from help and completion.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets whether values are sensitive.</summary>
    public bool IsSensitive { get; set; }

    /// <summary>Gets or sets the argument description.</summary>
    public string Description { get; set; } = string.Empty;
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
}

/// <summary>Declares an ordered command or nested-pack inclusion.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliPackAttribute(Type packType, string key, int order) : Attribute
{
    /// <summary>Gets the included option-pack declaration type.</summary>
    public Type PackType { get; } = packType;

    /// <summary>Gets the stable inclusion key.</summary>
    public string Key { get; } = key;

    /// <summary>Gets the explicit inclusion order.</summary>
    public int Order { get; } = order;

    /// <summary>Gets or sets the optional help-group title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional help-group description.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>Declares an ordered option definition owned by a generated pack.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CliPackOptionAttribute(string name, Type valueType, OptionArity arity, int order) : Attribute
{
    /// <summary>Gets the canonical option spelling.</summary>
    public string Name { get; } = name;

    /// <summary>Gets the value type.</summary>
    public Type ValueType { get; } = valueType;

    /// <summary>Gets the option arity.</summary>
    public OptionArity Arity { get; } = arity;

    /// <summary>Gets the explicit declaration order.</summary>
    public int Order { get; } = order;

    /// <summary>Gets or sets option aliases.</summary>
    public string[] Aliases { get; set; } = [];

    /// <summary>Gets or sets whether the option is required.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Gets or sets whether null values are allowed.</summary>
    public bool AllowsNull { get; set; }

    /// <summary>Gets or sets whether the option is hidden from help and completion.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets whether values are sensitive.</summary>
    public bool IsSensitive { get; set; }

    /// <summary>Gets or sets the option description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the display label for a value.</summary>
    public string ValueLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the inherited visibility scope.</summary>
    public OptionScope Scope { get; set; }

    /// <summary>Gets or sets the behavior for an optional-valued option used without a value.</summary>
    public BareValuePolicy BareValuePolicy { get; set; }
}
