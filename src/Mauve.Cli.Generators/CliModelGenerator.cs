using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mauve.Cli.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class CliModelGenerator : IIncrementalGenerator
{
    private const string CommandAttributeName = "Mauve.Cli.Declarations.CliCommandAttribute";
    private const string ChildAttributeName = "Mauve.Cli.Declarations.CliChildAttribute";
    private const string OptionAttributeName = "Mauve.Cli.Declarations.CliOptionAttribute";
    private const string ArgumentAttributeName = "Mauve.Cli.Declarations.CliArgumentAttribute";
    private const string HandlerAttributeName = "Mauve.Cli.Declarations.CliHandlerAttribute";
    private const string ContextType = "global::Mauve.Cli.Invocation.CommandInvocationContext";

    private static readonly DiagnosticDescriptor InvalidDeclaration = new(
        "MCLI001",
        "Invalid CLI declaration",
        "{0}",
        "Mauve.Cli.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidMember = new(
        "MCLI002",
        "Invalid CLI member",
        "{0}",
        "Mauve.Cli.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidHierarchy = new(
        "MCLI003",
        "Invalid CLI hierarchy",
        "{0}",
        "Mauve.Cli.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> declarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CommandAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        context.RegisterSourceOutput(declarations.Collect(), static (productionContext, symbols) =>
            Generate(productionContext, symbols));
    }

    private static void Generate(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> declaredTypes)
    {
        Dictionary<INamedTypeSymbol, CommandInfo> commands = new(SymbolEqualityComparer.Default);
        foreach (INamedTypeSymbol? type in declaredTypes.Distinct(SymbolEqualityComparer.Default)
            .OrderBy(static symbol => symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty, StringComparer.Ordinal))
        {
            if (type is null)
            {
                continue;
            }

            AttributeData? attribute = FindAttribute(type, CommandAttributeName);
            if (attribute is null)
            {
                continue;
            }

            CommandInfo command = CreateCommandInfo(context, type, attribute);
            commands.Add(type, command);
        }

        foreach (CommandInfo command in commands.Values.OrderBy(static item =>
            item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            ReadChildren(context, command, commands);
        }

        foreach (CommandInfo command in commands.Values
            .Where(static item => !item.IsRoot)
            .OrderBy(static item => item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            if (command.IsValid)
            {
                context.AddSource(
                    GetHintName(command.Symbol, ".Members"),
                    SourceText.From(GenerateMembers(command), Encoding.UTF8));
            }
        }

        foreach (CommandInfo root in commands.Values
            .Where(static item => item.IsRoot)
            .OrderBy(static item => item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            HashSet<INamedTypeSymbol> active = new(SymbolEqualityComparer.Default);
            HashSet<string> pathOptionNames = new(StringComparer.Ordinal);
            if (!ValidateTree(context, root, active, pathOptionNames))
            {
                continue;
            }

            context.AddSource(
                GetHintName(root.Symbol, ".Members"),
                SourceText.From(GenerateMembers(root), Encoding.UTF8));
        }
    }

    private static CommandInfo CreateCommandInfo(
        SourceProductionContext context,
        INamedTypeSymbol symbol,
        AttributeData attribute)
    {
        string name = GetConstructorString(attribute, 0) ?? string.Empty;
        bool isRoot = GetNamedBoolean(attribute, "IsRoot");
        CommandInfo command = new(
            symbol,
            attribute,
            name,
            isRoot,
            GetNamedString(attribute, "Description") ?? string.Empty,
            GetNamedString(attribute, "LongDescription") ?? string.Empty,
            GetNamedBoolean(attribute, "IsHidden"),
            GetNamedStrings(attribute, "Aliases"));

        if (symbol.TypeKind != TypeKind.Class
            || symbol.IsStatic
            || symbol.IsGenericType
            || symbol.ContainingType is not null)
        {
            Report(context, InvalidDeclaration, GetLocation(attribute, symbol), "Commands must be non-generic, non-static, top-level classes.");
            command.IsValid = false;
        }

        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ClassDeclarationSyntax declaration
                || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                Report(context, InvalidDeclaration, reference.GetSyntax().GetLocation(), "Command declarations must be partial classes.");
                command.IsValid = false;
            }
        }

        string[] classNames = [name, .. command.Aliases];
        bool validName = isRoot ? IsValidRootName(name) : IsValidCommandName(name);
        if (!validName
            || command.Aliases.Any(static alias => !IsValidCommandName(alias))
            || classNames.Distinct(StringComparer.Ordinal).Count() != classNames.Length)
        {
            Report(context, InvalidDeclaration, GetLocation(attribute, symbol), "Command names and aliases must follow the supported naming rules and be unique.");
            command.IsValid = false;
        }

        foreach (IPropertySymbol property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            AttributeData? optionAttribute = FindAttribute(property, OptionAttributeName);
            AttributeData? argumentAttribute = FindAttribute(property, ArgumentAttributeName);
            if (optionAttribute is null && argumentAttribute is null)
            {
                continue;
            }

            if (optionAttribute is not null && argumentAttribute is not null)
            {
                Report(context, InvalidMember, GetLocation(optionAttribute, property), "A property cannot be both an option and a positional argument.");
                command.IsValid = false;
                continue;
            }

            if (!ValidateProperty(context, property, optionAttribute ?? argumentAttribute!))
            {
                command.IsValid = false;
                continue;
            }

            int order = GetConstructorInt(optionAttribute ?? argumentAttribute!, 2 - (argumentAttribute is null ? 0 : 1));
            if (optionAttribute is not null)
            {
                command.Options.Add(new OptionInfo(property, optionAttribute, order));
            }
            else
            {
                command.Arguments.Add(new ArgumentInfo(property, argumentAttribute!, order));
            }
        }

        command.Options.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        command.Arguments.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        ValidateOrders(context, command.Options.Select(static item => (item.Order, GetLocation(item.Attribute, item.Property))).ToArray(), command);
        ValidateOrders(context, command.Arguments.Select(static item => (item.Order, GetLocation(item.Attribute, item.Property))).ToArray(), command);

        IMethodSymbol[] handlers = symbol.GetMembers().OfType<IMethodSymbol>()
            .Where(static method => FindAttribute(method, HandlerAttributeName) is not null)
            .ToArray();
        if (handlers.Length > 1)
        {
            Report(context, InvalidMember, GetLocation(FindAttribute(handlers[1], HandlerAttributeName), handlers[1]), "A command can declare at most one handler.");
            command.IsValid = false;
        }
        else if (handlers.Length == 1)
        {
            command.Handler = handlers[0];
            if (!ValidateHandler(context, command, handlers[0]))
            {
                command.IsValid = false;
            }
        }

        if (isRoot && symbol.GetMembers("CreateModel").OfType<IMethodSymbol>().Any())
        {
            Report(context, InvalidDeclaration, GetLocation(attribute, symbol), "The generated CreateModel method conflicts with an existing member.");
            command.IsValid = false;
        }

        return command;
    }

    private static bool ValidateProperty(
        SourceProductionContext context,
        IPropertySymbol property,
        AttributeData attribute)
    {
        string declaredName = GetConstructorString(attribute, 0) ?? string.Empty;
        if (string.Equals(attribute.AttributeClass?.ToDisplayString(), OptionAttributeName, StringComparison.Ordinal))
        {
            string[] spellings = [declaredName, .. GetNamedStrings(attribute, "Aliases")];
            if (!IsValidLongOptionName(declaredName)
                || GetNamedStrings(attribute, "Aliases").Any(static alias => !IsValidOptionAlias(alias))
                || spellings.Distinct(StringComparer.Ordinal).Count() != spellings.Length)
            {
                Report(context, InvalidMember, GetLocation(attribute, property), "Option names and aliases must be valid and unique.");
                return false;
            }
        }
        else if (!IsValidCommandName(declaredName))
        {
            Report(context, InvalidMember, GetLocation(attribute, property), "Positional names must follow the supported command-name rules.");
            return false;
        }

        bool hasPartialDeclaration = property.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
        bool hasUserImplementation = property.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(static declaration => declaration.ExpressionBody is not null
                || declaration.AccessorList?.Accessors.Any(static accessor =>
                    accessor.Body is not null || accessor.ExpressionBody is not null) == true);
        if (!hasPartialDeclaration
            || hasUserImplementation
            || property.IsStatic
            || property.IsIndexer
            || property.ReturnsByRef
            || property.ReturnsByRefReadonly
            || property.GetMethod is null
            || property.SetMethod is not null
            || property.Type.TypeKind == TypeKind.Dynamic
            || property.Type.TypeKind == TypeKind.Pointer)
        {
            Report(context, InvalidMember, GetLocation(attribute, property), "A bound member must be a partial, get-only, non-static instance property with a supported value type.");
            return false;
        }

        if (property.Type.IsReferenceType && property.Type.NullableAnnotation == NullableAnnotation.None)
        {
            Report(context, InvalidMember, GetLocation(attribute, property), "Bound reference properties require an explicit nullable or nonnullable annotation.");
            return false;
        }

        if (GetNamedBoolean(attribute, "AllowsNull") && !IsNullable(property.Type))
        {
            Report(context, InvalidMember, GetLocation(attribute, property), "A member that allows null values must use a nullable property type.");
            return false;
        }

        if (string.Equals(attribute.AttributeClass?.ToDisplayString(), OptionAttributeName, StringComparison.Ordinal))
        {
            int arity = GetConstructorInt(attribute, 1);
            int bareValuePolicy = GetNamedInt(attribute, "BareValuePolicy");
            bool collection = arity is 3 or 4;
            if ((collection && !TryGetCollectionElementType(property.Type, out _))
                || (!collection && arity is 0 or 1 && TryGetCollectionElementType(property.Type, out _))
                || (arity == 2 && property.Type.SpecialType != SpecialType.System_Boolean))
            {
                Report(context, InvalidMember, GetLocation(attribute, property), "Collection options require an IReadOnlyList<T> property, and boolean flags require a bool property.");
                return false;
            }

            if (arity is < 0 or > 4
                || GetNamedInt(attribute, "Scope") is < 0 or > 1
                || (arity == 1 && bareValuePolicy != 1)
                || (arity != 1 && bareValuePolicy != 0)
                || (bareValuePolicy == 1 && !IsNullable(property.Type))
                || (arity is 0 or 1 && !GetNamedBoolean(attribute, "IsRequired") && !IsNullable(property.Type)))
            {
                Report(context, InvalidMember, GetLocation(attribute, property), "Optional-valued options must use the supported null bare-value policy, and possibly absent scalar options require nullable properties.");
                return false;
            }
        }
        else
        {
            int minimum = GetNamedInt(attribute, "MinimumValues", 1);
            int maximum = GetNamedInt(attribute, "MaximumValues", 1);
            if (minimum < 0
                || (maximum != -1 && maximum < minimum)
                || (maximum != 1 && !TryGetCollectionElementType(property.Type, out _))
                || (maximum == 1 && TryGetCollectionElementType(property.Type, out _)))
            {
                Report(context, InvalidMember, GetLocation(attribute, property), "Single-value arguments require a scalar property; multi-value arguments require an IReadOnlyList<T> property.");
                return false;
            }

            if (minimum == 0 && !IsNullable(property.Type) && maximum == 1)
            {
                Report(context, InvalidMember, GetLocation(attribute, property), "An optional single-value argument requires a nullable property type.");
                return false;
            }
        }

        string generatedTryName = string.Concat("TryGet", property.Name);
        if (property.ContainingType.GetMembers(generatedTryName).Length > 0)
        {
            Report(context, InvalidMember, GetLocation(attribute, property), $"Generated accessor '{generatedTryName}' conflicts with an existing member.");
            return false;
        }

        return true;
    }

    private static bool ValidateHandler(
        SourceProductionContext context,
        CommandInfo command,
        IMethodSymbol handler)
    {
        bool supportedReturn = IsSupportedReturnType(handler.ReturnType);
        bool supportedParameters = handler.Parameters.Length == 0
            || (handler.Parameters.Length == 1
                && handler.Parameters[0].RefKind == RefKind.None
                && handler.Parameters[0].Type.ToDisplayString() == "System.Threading.CancellationToken");
        if (handler.IsStatic
            || handler.IsGenericMethod
            || (handler.IsAsync && handler.ReturnsVoid)
            || !supportedReturn
            || !supportedParameters
            || command.Symbol.IsAbstract
            || !command.Symbol.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0))
        {
            Report(context, InvalidMember, GetLocation(FindAttribute(handler, HandlerAttributeName), handler), "Handlers must be instance methods with no parameters or one CancellationToken, a supported return type and a parameterless command constructor.");
            return false;
        }

        return true;
    }

    private static bool IsSupportedReturnType(ITypeSymbol type)
    {
        string display = type.ToDisplayString();
        return display is "void"
            or "int"
            or "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.ValueTask"
            || type is INamedTypeSymbol named
                && named.TypeArguments.Length == 1
                && named.TypeArguments[0].SpecialType == SpecialType.System_Int32
                && named.OriginalDefinition.ToDisplayString() is "System.Threading.Tasks.Task<TResult>"
                    or "System.Threading.Tasks.ValueTask<TResult>";
    }

    private static void ReadChildren(
        SourceProductionContext context,
        CommandInfo command,
        Dictionary<INamedTypeSymbol, CommandInfo> commands)
    {
        foreach (AttributeData attribute in command.Symbol.GetAttributes()
            .Where(static item => item.AttributeClass?.ToDisplayString() == ChildAttributeName))
        {
            INamedTypeSymbol? childType = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
                : null;
            int order = GetConstructorInt(attribute, 1);
            if (childType is null || !commands.TryGetValue(childType, out CommandInfo? child))
            {
                Report(context, InvalidHierarchy, GetLocation(attribute, command.Symbol), "Every child type must have a CliCommand declaration in this compilation.");
                command.IsValid = false;
                continue;
            }

            if (child.IsRoot)
            {
                Report(context, InvalidHierarchy, GetLocation(attribute, command.Symbol), "A model root cannot be included as a child.");
                command.IsValid = false;
                continue;
            }

            command.Children.Add(new ChildInfo(command, child, attribute, order));
        }

        command.Children.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        ValidateOrders(context, command.Children.Select(static item => (item.Order, GetLocation(item.Attribute, item.Parent.Symbol))).ToArray(), command);
    }

    private static bool ValidateTree(
        SourceProductionContext context,
        CommandInfo command,
        HashSet<INamedTypeSymbol> active,
        HashSet<string> inheritedNames)
    {
        if (!active.Add(command.Symbol))
        {
            Report(context, InvalidHierarchy, GetLocation(command.Attribute, command.Symbol), "The command hierarchy contains a cycle.");
            return false;
        }

        bool valid = command.IsValid;
        HashSet<string> commandNames = new(StringComparer.Ordinal);
        foreach (ChildInfo child in command.Children)
        {
            if (!commandNames.Add(child.Child.Name))
            {
                Report(context, InvalidHierarchy, GetLocation(child.Attribute, command.Symbol), "Sibling command names or aliases must be unique.");
                valid = false;
            }

            foreach (string alias in child.Child.Aliases)
            {
                if (!commandNames.Add(alias))
                {
                    Report(context, InvalidHierarchy, GetLocation(child.Attribute, command.Symbol), "Sibling command names or aliases must be unique.");
                    valid = false;
                }
            }
        }

        HashSet<string> effective = new(inheritedNames, StringComparer.Ordinal);
        foreach (OptionInfo option in command.Options)
        {
            string name = GetConstructorString(option.Attribute, 0) ?? string.Empty;
            if (!effective.Add(name))
            {
                Report(context, InvalidHierarchy, GetLocation(option.Attribute, option.Property), "Visible option names and aliases must be unique.");
                valid = false;
            }

            foreach (string alias in GetNamedStrings(option.Attribute, "Aliases"))
            {
                if (!effective.Add(alias))
                {
                    Report(context, InvalidHierarchy, GetLocation(option.Attribute, option.Property), "Visible option names and aliases must be unique.");
                    valid = false;
                }

            }
        }

        HashSet<string> inheritedForChildren = new(inheritedNames, StringComparer.Ordinal);
        foreach (OptionInfo option in command.Options)
        {
            if (GetNamedInt(option.Attribute, "Scope") == 1)
            {
                inheritedForChildren.Add(GetConstructorString(option.Attribute, 0) ?? string.Empty);
                foreach (string alias in GetNamedStrings(option.Attribute, "Aliases"))
                {
                    inheritedForChildren.Add(alias);
                }
            }
        }

        foreach (ChildInfo child in command.Children)
        {
            valid &= ValidateTree(context, child.Child, active, inheritedForChildren);
        }

        _ = active.Remove(command.Symbol);
        return valid;
    }

    private static string GenerateMembers(CommandInfo command)
    {
        StringBuilder builder = new();
        string namespaceName = command.Symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : command.Symbol.ContainingNamespace.ToDisplayString();
        string suffix = GetStableSuffix(command.Symbol);
        string typeName = EscapeIdentifier(command.Symbol.Name);

        builder.AppendLine("#nullable enable");
        if (namespaceName.Length > 0)
        {
            builder.Append("namespace ").Append(namespaceName).AppendLine();
            builder.AppendLine("{");
        }

        builder.Append(AccessibilityText(command.Symbol.DeclaredAccessibility))
            .Append(" partial class ").Append(typeName).AppendLine();
        builder.AppendLine("{");

        for (int index = 0; index < command.Options.Count; index++)
        {
            OptionInfo option = command.Options[index];
            builder.Append("    private static readonly global::Mauve.Cli.Definitions.OptionDefinition ")
                .Append(OptionInfo.FieldName(suffix, index)).Append(" = ")
                .Append(GenerateOption(option)).AppendLine(";");
        }

        for (int index = 0; index < command.Arguments.Count; index++)
        {
            ArgumentInfo argument = command.Arguments[index];
            builder.Append("    private static readonly global::Mauve.Cli.Definitions.PositionalDefinition ")
                .Append(ArgumentInfo.FieldName(suffix, index)).Append(" = ")
                .Append(GenerateArgument(argument)).AppendLine(";");
        }

        string contextField = string.Concat("__mauveContext_", suffix);
        string attachName = string.Concat("__MauveAttach_", suffix);
        builder.Append("    private ").Append(ContextType).Append("? ").Append(contextField).AppendLine(";");
        builder.Append("    internal void ").Append(attachName).Append('(').Append(ContextType).AppendLine(" context)");
        builder.AppendLine("    {");
        builder.Append("        ").Append(contextField).AppendLine(" = context;");
        builder.AppendLine("    }");

        for (int index = 0; index < command.Options.Count; index++)
        {
            EmitPropertyBinding(
                builder,
                command.Options[index].Property,
                OptionInfo.FieldName(suffix, index),
                contextField,
                isOption: true);
        }

        for (int index = 0; index < command.Arguments.Count; index++)
        {
            EmitPropertyBinding(
                builder,
                command.Arguments[index].Property,
                ArgumentInfo.FieldName(suffix, index),
                contextField,
                isOption: false);
        }

        string buildName = string.Concat("__MauveBuildCommand_", suffix);
        builder.Append("    internal static global::Mauve.Cli.Definitions.CommandBuilder ").Append(buildName).AppendLine("()");
        builder.AppendLine("    {");
        builder.Append("        global::Mauve.Cli.Definitions.CommandBuilder command = new(")
            .Append(Literal(command.Name)).AppendLine(")");
        builder.AppendLine("        {");
        builder.Append("            Description = ").Append(Literal(command.Description)).AppendLine(",");
        builder.Append("            LongDescription = ").Append(Literal(command.LongDescription)).AppendLine(",");
        builder.Append("            IsHidden = ").Append(command.IsHidden ? "true" : "false").AppendLine();
        builder.AppendLine("        };");
        foreach (string alias in command.Aliases)
        {
            builder.Append("        _ = command.AddAlias(").Append(Literal(alias)).AppendLine(");");
        }

        for (int index = 0; index < command.Options.Count; index++)
        {
            builder.Append("        _ = command.AddOption(").Append(OptionInfo.FieldName(suffix, index)).AppendLine(");");
        }

        for (int index = 0; index < command.Arguments.Count; index++)
        {
            builder.Append("        _ = command.AddPositional(").Append(ArgumentInfo.FieldName(suffix, index)).AppendLine(");");
        }

        foreach (ChildInfo child in command.Children)
        {
            builder.Append("        _ = command.AddChild(")
                .Append(child.Child.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append('.').Append(string.Concat("__MauveBuildCommand_", GetStableSuffix(child.Child.Symbol)))
                .AppendLine("());");
        }

        if (command.Handler is not null)
        {
            builder.Append("        _ = command.SetValueTaskHandlerWithExitCode(__MauveInvoke_")
                .Append(suffix).AppendLine(");");
        }

        builder.AppendLine("        return command;");
        builder.AppendLine("    }");

        if (command.Handler is not null)
        {
            EmitHandler(builder, command, suffix, attachName);
        }

        if (command.IsRoot)
        {
            builder.AppendLine("    public static global::Mauve.Cli.Definitions.CommandModel CreateModel(");
            builder.AppendLine("        global::Mauve.Cli.Definitions.CliLimits? limits = null,");
            builder.AppendLine("        bool includeStandardHelp = false)");
            builder.AppendLine("    {");
            builder.Append("        return global::Mauve.Cli.Definitions.CommandModel.Create(")
                .Append(buildName).AppendLine("(), limits, includeStandardHelp);");
            builder.AppendLine("    }");
        }

        builder.AppendLine("}");
        if (namespaceName.Length > 0)
        {
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    private static string GenerateOption(OptionInfo option)
    {
        string name = GetConstructorString(option.Attribute, 0) ?? string.Empty;
        int arityValue = GetConstructorInt(option.Attribute, 1);
        string arity = EnumName("OptionArity", arityValue);
        string typeForTypeof = FormatTypeForTypeof(option.Property.Type);
        string elementType = string.Empty;
        if (arityValue is 3 or 4
            && TryGetCollectionElementType(option.Property.Type, out ITypeSymbol? optionElement)
            && optionElement is not null)
        {
            elementType = string.Concat(", elementType: typeof(", FormatTypeForTypeof(optionElement), ")");
        }
        string aliases = string.Join(
            ", ",
            GetNamedStrings(option.Attribute, "Aliases")
                .Select(alias => $"new global::Mauve.Cli.Definitions.OptionAlias({Literal(alias)})"));
        string barePolicy = GetNamedInt(option.Attribute, "BareValuePolicy") == 1
            ? ", bareValuePolicy: global::Mauve.Cli.Definitions.BareValuePolicy.Null"
            : string.Empty;
        return string.Concat(
            "new global::Mauve.Cli.Definitions.OptionDefinition(",
            Literal(name),
            ", typeof(",
            typeForTypeof,
            "), global::Mauve.Cli.Definitions.OptionArity.",
            arity,
            elementType,
            ", aliases: new global::Mauve.Cli.Definitions.OptionAlias[] { ",
            aliases,
            " }, allowsNull: ",
            GetNamedBoolean(option.Attribute, "AllowsNull") || IsNullable(option.Property.Type) ? "true" : "false",
            ", isRequired: ",
            GetNamedBoolean(option.Attribute, "IsRequired") ? "true" : "false",
            ", description: ",
            Literal(GetNamedString(option.Attribute, "Description") ?? string.Empty),
            ", valueLabel: ",
            Literal(GetNamedString(option.Attribute, "ValueLabel") ?? string.Empty),
            ", isHidden: ",
            GetNamedBoolean(option.Attribute, "IsHidden") ? "true" : "false",
            ", isSensitive: ",
            GetNamedBoolean(option.Attribute, "IsSensitive") ? "true" : "false",
            ", scope: global::Mauve.Cli.Definitions.OptionScope.",
            EnumName("OptionScope", GetNamedInt(option.Attribute, "Scope")),
            barePolicy,
            ")");
    }

    private static string GenerateArgument(ArgumentInfo argument)
    {
        string name = GetConstructorString(argument.Attribute, 0) ?? string.Empty;
        int maximum = GetNamedInt(argument.Attribute, "MaximumValues", 1);
        string maximumText = maximum < 0 ? "null" : maximum.ToString(CultureInfo.InvariantCulture);
        string typeForTypeof = FormatTypeForTypeof(argument.Property.Type);
        string element = maximum != 1
            ? string.Concat(", elementType: typeof(", GetCollectionElementType(argument.Property.Type), ")")
            : string.Empty;
        return string.Concat(
            "new global::Mauve.Cli.Definitions.PositionalDefinition(",
            Literal(name),
            ", typeof(",
            typeForTypeof,
            "), minimumValues: ",
            GetNamedInt(argument.Attribute, "MinimumValues", 1).ToString(CultureInfo.InvariantCulture),
            ", maximumValues: ",
            maximumText,
            element,
            ", allowsNull: ",
            GetNamedBoolean(argument.Attribute, "AllowsNull") || IsNullable(argument.Property.Type) ? "true" : "false",
            ", description: ",
            Literal(GetNamedString(argument.Attribute, "Description") ?? string.Empty),
            ", isHidden: ",
            GetNamedBoolean(argument.Attribute, "IsHidden") ? "true" : "false",
            ", isSensitive: ",
            GetNamedBoolean(argument.Attribute, "IsSensitive") ? "true" : "false",
            ")");
    }

    private static void EmitPropertyBinding(
        StringBuilder builder,
        IPropertySymbol property,
        string definitionField,
        string contextField,
        bool isOption)
    {
        string propertyType = FormatType(property.Type);
        string propertyName = EscapeIdentifier(property.Name);
        string tryName = EscapeIdentifier(string.Concat("TryGet", property.Name));
        string accessibility = AccessibilityText(property.DeclaredAccessibility);
        builder.Append("    ").Append(accessibility).Append(" partial ").Append(propertyType).Append(' ')
            .Append(propertyName).AppendLine();
        builder.AppendLine("    {");
        builder.AppendLine("        get");
        builder.AppendLine("        {");
        builder.Append("            if (!").Append(tryName).AppendLine("(out var value))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.InvalidOperationException(\"The command value is unavailable.\");");
        builder.AppendLine("            }");
        builder.AppendLine("            return value;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.Append("    ").Append(accessibility).Append(" bool ").Append(tryName)
            .Append("(out ").Append(propertyType).AppendLine(" value)");
        builder.AppendLine("    {");
        builder.Append("        global::Mauve.Cli.Invocation.CommandInvocationContext context = ")
            .Append(contextField)
            .AppendLine(" ?? throw new global::System.InvalidOperationException(\"Command binding is not attached.\");");
        builder.Append("        return context.")
            .Append(isOption ? "TryGetOptionValue<" : "TryGetPositionalValue<")
            .Append(propertyType)
            .Append(">(").Append(definitionField).AppendLine(", out value);");
        builder.AppendLine("    }");
    }

    private static void EmitHandler(
        StringBuilder builder,
        CommandInfo command,
        string suffix,
        string attachName)
    {
        IMethodSymbol handler = command.Handler!;
        string handlerName = EscapeIdentifier(handler.Name);
        bool takesToken = handler.Parameters.Length == 1;
        string arguments = takesToken ? "cancellationToken" : string.Empty;
        string invocation = string.Concat("instance.", handlerName, "(", arguments, ")");
        string fullyQualifiedTypeName = command.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string returnType = handler.ReturnType.ToDisplayString();
        builder.Append("    private static ");
        bool isAsync = returnType.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal);
        if (isAsync)
        {
            builder.Append("async ");
        }

        builder.Append("global::System.Threading.Tasks.ValueTask<int> __MauveInvoke_")
            .Append(suffix).Append('(').Append(ContextType).AppendLine(" context, global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");
        builder.Append("        ").Append(fullyQualifiedTypeName).AppendLine(" instance = new();");
        builder.Append("        instance.").Append(attachName).AppendLine("(context);");
        if (returnType == "void")
        {
            builder.Append("        ").Append(invocation).AppendLine(";");
            builder.AppendLine("        return new global::System.Threading.Tasks.ValueTask<int>(0);");
        }
        else if (returnType == "int")
        {
            builder.Append("        return new global::System.Threading.Tasks.ValueTask<int>(").Append(invocation).AppendLine(");");
        }
        else if (returnType == "System.Threading.Tasks.Task")
        {
            builder.Append("        global::System.Threading.Tasks.Task task = ").Append(invocation).AppendLine(";");
            builder.AppendLine("        if (task is null) throw new global::System.InvalidOperationException(\"The asynchronous handler returned a null task.\");");
            builder.AppendLine("        await task.ConfigureAwait(false);");
            builder.AppendLine("        return 0;");
        }
        else if (returnType == "System.Threading.Tasks.Task<int>")
        {
            builder.Append("        global::System.Threading.Tasks.Task<int> task = ").Append(invocation).AppendLine(";");
            builder.AppendLine("        if (task is null) throw new global::System.InvalidOperationException(\"The asynchronous handler returned a null task.\");");
            builder.AppendLine("        return await task.ConfigureAwait(false);");
        }
        else if (returnType == "System.Threading.Tasks.ValueTask")
        {
            builder.Append("        await ").Append(invocation).AppendLine(".ConfigureAwait(false);");
            builder.AppendLine("        return 0;");
        }
        else
        {
            builder.Append("        return await ").Append(invocation).AppendLine(".ConfigureAwait(false);");
        }

        builder.AppendLine("    }");
    }

    private static void ValidateOrders(
        SourceProductionContext context,
        (int Order, Location Location)[] values,
        CommandInfo command)
    {
        HashSet<int> orders = [];
        foreach ((int order, Location location) in values)
        {
            if (order < 0 || !orders.Add(order))
            {
                Report(context, InvalidMember, location, $"Order values on '{command.Name}' must be unique and nonnegative.");
                command.IsValid = false;
            }
        }
    }

    private static string GetCollectionElementType(ITypeSymbol type)
    {
        return TryGetCollectionElementType(type, out ITypeSymbol? elementType) && elementType is not null
            ? FormatTypeForTypeof(elementType)
            : "global::System.Object";
    }

    private static bool IsValidRootName(string name)
    {
        return name.Length is > 0 and <= 128
            && !name.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character));
    }

    private static bool IsValidCommandName(string name)
    {
        if (name.Length is 0 or > 128 || !IsAsciiAlphaNumeric(name[0]))
        {
            return false;
        }

        return name.Skip(1).All(static character => IsAsciiAlphaNumeric(character) || character == '-');
    }

    private static bool IsValidLongOptionName(string name)
    {
        return name.Length is >= 3 and <= 128
            && name.StartsWith("--", StringComparison.Ordinal)
            && IsAsciiLetter(name[2])
            && name.Skip(3).All(static character => IsAsciiAlphaNumeric(character) || character == '-');
    }

    private static bool IsValidOptionAlias(string name)
    {
        return (name.Length == 2 && name[0] == '-' && IsAsciiLetter(name[1]))
            || IsValidLongOptionName(name);
    }

    private static bool IsAsciiAlphaNumeric(char character)
    {
        return IsAsciiLetter(character) || character is >= '0' and <= '9';
    }

    private static bool IsAsciiLetter(char character)
    {
        return character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
    }

    private static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        if (type is INamedTypeSymbol named
            && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyList<T>")
        {
            elementType = named.TypeArguments[0];
            return true;
        }

        elementType = null;
        return false;
    }

    private static bool IsNullable(ITypeSymbol type)
    {
        return type.NullableAnnotation == NullableAnnotation.Annotated
            || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static string FormatType(ITypeSymbol type)
    {
        return type.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                    | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
    }

    private static string FormatTypeForTypeof(ITypeSymbol type)
    {
        return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string Literal(string value)
    {
        return SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private static string EnumName(string enumName, int value)
    {
        string[] names = enumName switch
        {
            "OptionArity" => ["RequiredScalar", "OptionalScalar", "BooleanFlag", "RepeatedSingleCollection", "GreedyCollection"],
            "OptionScope" => ["Local", "Inherited"],
            _ => []
        };
        return (uint)value < (uint)names.Length ? names[value] : names[0];
    }

    private static string AccessibilityText(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "private"
        };
    }

    private static string EscapeIdentifier(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
                ? string.Concat("@", name)
                : name;
    }

    private static string GetStableSuffix(INamedTypeSymbol symbol)
    {
        string identity = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        uint hash = 2166136261;
        foreach (char character in identity)
        {
            hash = unchecked((hash ^ character) * 16777619);
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    private static string GetHintName(INamedTypeSymbol symbol, string suffix)
    {
        string identity = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        StringBuilder name = new();
        foreach (char character in identity)
        {
            name.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return string.Concat(name, "_", GetStableSuffix(symbol), suffix, ".g.cs");
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().FirstOrDefault(attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal));
    }

    private static Location GetLocation(AttributeData? attribute, ISymbol fallback)
    {
        return attribute?.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? fallback.Locations.FirstOrDefault()
            ?? Location.None;
    }

    private static string? GetConstructorString(AttributeData attribute, int index)
    {
        return attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;
    }

    private static int GetConstructorInt(AttributeData attribute, int index)
    {
        return attribute.ConstructorArguments.Length > index
            ? Convert.ToInt32(attribute.ConstructorArguments[index].Value, CultureInfo.InvariantCulture)
            : -1;
    }

    private static string? GetNamedString(AttributeData attribute, string name)
    {
        return attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value as string;
    }

    private static int GetNamedInt(AttributeData attribute, string name, int defaultValue = 0)
    {
        KeyValuePair<string, TypedConstant> value = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name);
        return value.Key is null
            ? defaultValue
            : Convert.ToInt32(value.Value.Value, CultureInfo.InvariantCulture);
    }

    private static bool GetNamedBoolean(AttributeData attribute, string name)
    {
        return attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value is true;
    }

    private static string[] GetNamedStrings(AttributeData attribute, string name)
    {
        TypedConstant constant = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value;
        return constant.Kind == TypedConstantKind.Array
            ? [.. constant.Values.Select(static value => value.Value as string ?? string.Empty)]
            : [];
    }

    private static void Report(
        SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        Location location,
        string message)
    {
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));
    }

    private sealed class CommandInfo(
        INamedTypeSymbol symbol,
        AttributeData attribute,
        string name,
        bool isRoot,
        string description,
        string longDescription,
        bool isHidden,
        string[] aliases)
    {
        public INamedTypeSymbol Symbol { get; } = symbol;

        public AttributeData Attribute { get; } = attribute;

        public string Name { get; } = name;

        public bool IsRoot { get; } = isRoot;

        public string Description { get; } = description;

        public string LongDescription { get; } = longDescription;

        public bool IsHidden { get; } = isHidden;

        public string[] Aliases { get; } = aliases;

        public List<OptionInfo> Options { get; } = [];

        public List<ArgumentInfo> Arguments { get; } = [];

        public List<ChildInfo> Children { get; } = [];

        public IMethodSymbol? Handler { get; set; }

        public bool IsValid { get; set; } = true;
    }

    private sealed class OptionInfo(IPropertySymbol property, AttributeData attribute, int order)
    {
        public IPropertySymbol Property { get; } = property;

        public AttributeData Attribute { get; } = attribute;

        public int Order { get; } = order;

        public static string FieldName(string suffix, int index)
        {
            return string.Concat("__mauveOption_", suffix, "_", index.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class ArgumentInfo(IPropertySymbol property, AttributeData attribute, int order)
    {
        public IPropertySymbol Property { get; } = property;

        public AttributeData Attribute { get; } = attribute;

        public int Order { get; } = order;

        public static string FieldName(string suffix, int index)
        {
            return string.Concat("__mauveArgument_", suffix, "_", index.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class ChildInfo(CommandInfo parent, CommandInfo child, AttributeData attribute, int order)
    {
        public CommandInfo Parent { get; } = parent;

        public CommandInfo Child { get; } = child;

        public AttributeData Attribute { get; } = attribute;

        public int Order { get; } = order;

    }
}
