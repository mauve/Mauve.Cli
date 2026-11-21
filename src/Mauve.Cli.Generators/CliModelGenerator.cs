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

/// <summary>Generates command models from attributed partial declarations.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class CliModelGenerator : IIncrementalGenerator
{
    private const string _commandAttributeName = "Mauve.Cli.Declarations.CliCommandAttribute";
    private const string _childAttributeName = "Mauve.Cli.Declarations.CliChildAttribute`1";
    private const string _optionAttributeName = "Mauve.Cli.Declarations.CliOptionAttribute";
    private const string _argumentAttributeName = "Mauve.Cli.Declarations.CliArgumentAttribute";
    private const string _handlerAttributeName = "Mauve.Cli.Declarations.CliHandlerAttribute";
    private const string _optionPackAttributeName = "Mauve.Cli.Declarations.CliOptionPackAttribute";
    private const string _packAttributeName = "Mauve.Cli.Declarations.CliPackAttribute`1";
    private const string _packOptionAttributeName = "Mauve.Cli.Declarations.CliPackOptionAttribute`1";
    private const string _contextType = "global::Mauve.Cli.Invocation.CommandInvocationContext";

    private static readonly DiagnosticDescriptor _invalidDeclaration = new(
        "MCLI001",
        "Invalid CLI declaration",
        "{0}",
        "Mauve.Cli.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _invalidMember = new(
        "MCLI002",
        "Invalid CLI member",
        "{0}",
        "Mauve.Cli.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor _invalidHierarchy = new(
        "MCLI003",
        "Invalid CLI hierarchy",
        "{0}",
        "Mauve.Cli.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Registers the declaration discovery and source-output pipelines.</summary>
    /// <param name="context">The compiler-provided initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<INamedTypeSymbol> commandDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                _commandAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        IncrementalValuesProvider<INamedTypeSymbol> packDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                _optionPackAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        context.RegisterSourceOutput(
            commandDeclarations.Collect()
                .Combine(packDeclarations.Collect())
                .Combine(context.CompilationProvider),
            static (productionContext, input) =>
                Generate(productionContext, input.Left.Left, input.Left.Right, input.Right));
    }

    private static void Generate(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> declaredTypes,
        ImmutableArray<INamedTypeSymbol> declaredPackTypes,
        Compilation compilation)
    {
        Dictionary<SyntaxTree, int> syntaxTreeOrder = [];
        int syntaxTreeIndex = 0;
        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees)
        {
            syntaxTreeOrder.Add(syntaxTree, syntaxTreeIndex++);
        }

        Dictionary<INamedTypeSymbol, CommandInfo> commands = [with(SymbolEqualityComparer.Default)];
        Dictionary<INamedTypeSymbol, PackInfo> packs = [with(SymbolEqualityComparer.Default)];
        foreach (INamedTypeSymbol? type in declaredPackTypes.Distinct(SymbolEqualityComparer.Default)
            .OrderBy(static symbol => symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty, StringComparer.Ordinal).Cast<INamedTypeSymbol?>())
        {
            if (type is not null)
            {
                AttributeData? attribute = FindAttribute(type, _optionPackAttributeName);
                if (attribute is not null)
                {
                    packs.Add(type, CreatePackInfo(context, type, attribute, syntaxTreeOrder));
                }
            }
        }

        foreach (INamedTypeSymbol? type in declaredTypes.Distinct(SymbolEqualityComparer.Default)
            .OrderBy(static symbol => symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty, StringComparer.Ordinal).Cast<INamedTypeSymbol?>())
        {
            if (type is null)
            {
                continue;
            }

            AttributeData? attribute = FindAttribute(type, _commandAttributeName);
            if (attribute is null)
            {
                continue;
            }

            CommandInfo command = CreateCommandInfo(context, type, attribute, syntaxTreeOrder);
            commands.Add(type, command);
        }

        foreach (PackInfo pack in packs.Values)
        {
            ReadPackInclusions(context, pack, packs, syntaxTreeOrder);
        }

        foreach (CommandInfo command in commands.Values.OrderBy(static item =>
            item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            ReadChildren(context, command, commands, syntaxTreeOrder);
            ReadCommandPacks(context, command, packs, syntaxTreeOrder);
        }

        List<CommandInfo> validRoots = [];
        HashSet<CommandInfo> validCommands = [];
        foreach (CommandInfo root in commands.Values
            .Where(static item => item.IsRoot)
            .OrderBy(static item => item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            HashSet<INamedTypeSymbol> active = [with(SymbolEqualityComparer.Default)];
            HashSet<string> pathOptionNames = [with(StringComparer.Ordinal)];
            HashSet<INamedTypeSymbol> activePacks = [with(SymbolEqualityComparer.Default)];
            if (!ValidateTree(context, root, active, pathOptionNames, activePacks))
            {
                continue;
            }

            validRoots.Add(root);
            AddCommandTree(root, validCommands);
        }

        foreach (PackInfo pack in packs.Values.OrderBy(static item =>
            item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            HashSet<INamedTypeSymbol> active = [with(SymbolEqualityComparer.Default)];
            HashSet<string> names = [with(StringComparer.Ordinal)];
            HashSet<string> inheritedNames = [with(StringComparer.Ordinal)];
            _ = active.Add(pack.Symbol);
            bool valid = pack.IsValid
                & ValidatePackInclusions(context, pack.Inclusions, active, names, inheritedNames)
                & ValidatePackOptions(context, pack.Options, pack.Symbol, names, inheritedNames);
            _ = active.Remove(pack.Symbol);
            if (valid)
            {
                context.AddSource(
                    GetHintName(pack.Symbol, ".Pack"),
                    SourceText.From(GeneratePackMembers(pack), Encoding.UTF8));
            }
        }

        foreach (CommandInfo command in commands.Values
            .Where(item => !item.IsRoot && validCommands.Contains(item))
            .OrderBy(static item => item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            if (command.IsValid)
            {
                context.AddSource(
                    GetHintName(command.Symbol, ".Members"),
                    SourceText.From(GenerateMembers(command), Encoding.UTF8));
            }
        }

        foreach (CommandInfo root in validRoots)
        {
            context.AddSource(
                GetHintName(root.Symbol, ".Members"),
                SourceText.From(GenerateMembers(root), Encoding.UTF8));
        }
    }

    private static void AddCommandTree(CommandInfo root, HashSet<CommandInfo> commands)
    {
        Stack<CommandInfo> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            CommandInfo current = pending.Pop();
            if (!commands.Add(current))
            {
                continue;
            }

            foreach (ChildInfo child in current.Children)
            {
                pending.Push(child.Child);
            }
        }
    }

    private static CommandInfo CreateCommandInfo(
        SourceProductionContext context,
        INamedTypeSymbol symbol,
        AttributeData attribute,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder)
    {
        string name = GetConstructorString(attribute, 0) ?? DeriveCommandName(symbol.Name);
        bool isRoot = GetNamedBoolean(attribute, "IsRoot");
        CommandInfo command = new(
            symbol,
            attribute,
            name,
            isRoot,
            GetNamedString(attribute, "Description"),
            GetNamedString(attribute, "LongDescription"),
            GetNamedBoolean(attribute, "IsHidden"),
            GetNamedStrings(attribute, "Aliases"));

        if (symbol.TypeKind != TypeKind.Class
            || symbol.IsStatic
            || symbol.IsGenericType
            || symbol.ContainingType is not null)
        {
            Report(context, _invalidDeclaration, GetLocation(attribute, symbol), "Commands must be non-generic, non-static, top-level classes.");
            command.IsValid = false;
        }

        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ClassDeclarationSyntax declaration
                || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                Report(context, _invalidDeclaration, reference.GetSyntax().GetLocation(), "Command declarations must be partial classes.");
                command.IsValid = false;
            }
        }

        string[] classNames = [name, .. command.Aliases];
        bool validName = isRoot ? IsValidRootName(name) : IsValidCommandName(name);
        if (!validName
            || command.Aliases.Any(static alias => !IsValidCommandName(alias))
            || classNames.Distinct(StringComparer.Ordinal).Count() != classNames.Length)
        {
            Report(context, _invalidDeclaration, GetLocation(attribute, symbol), "Command names and aliases must follow the supported naming rules and be unique.");
            command.IsValid = false;
        }

        foreach (IPropertySymbol property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            AttributeData? optionAttribute = FindAttribute(property, _optionAttributeName);
            AttributeData? argumentAttribute = FindAttribute(property, _argumentAttributeName);
            if (optionAttribute is null && argumentAttribute is null)
            {
                continue;
            }

            if (optionAttribute is not null && argumentAttribute is not null)
            {
                Report(context, _invalidMember, GetLocation(optionAttribute, property), "A property cannot be both an option and a positional argument.");
                command.IsValid = false;
                continue;
            }

            if (!ValidateProperty(context, property, optionAttribute ?? argumentAttribute!))
            {
                command.IsValid = false;
                continue;
            }

            SourceOrder sourceOrder = GetSourceOrder(
                optionAttribute ?? argumentAttribute!,
                property,
                syntaxTreeOrder);
            if (optionAttribute is not null)
            {
                command.Options.Add(new OptionInfo(property, optionAttribute, sourceOrder));
            }
            else
            {
                command.Arguments.Add(new ArgumentInfo(property, argumentAttribute!, sourceOrder));
            }
        }

        command.Options.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));
        command.Arguments.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));

        IMethodSymbol[] handlers = [.. symbol.GetMembers().OfType<IMethodSymbol>().Where(static method => FindAttribute(method, _handlerAttributeName) is not null)];
        if (handlers.Length > 1)
        {
            Report(context, _invalidMember, GetLocation(FindAttribute(handlers[1], _handlerAttributeName), handlers[1]), "A command can declare at most one handler.");
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

        if (isRoot
            && (symbol.GetMembers("CreateModel").OfType<IMethodSymbol>().Any()
                || symbol.GetMembers("CreateBuilder").OfType<IMethodSymbol>().Any()))
        {
            Report(context, _invalidDeclaration, GetLocation(attribute, symbol), "A generated root factory conflicts with an existing member.");
            command.IsValid = false;
        }

        return command;
    }

    private static PackInfo CreatePackInfo(
        SourceProductionContext context,
        INamedTypeSymbol symbol,
        AttributeData attribute,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder)
    {
        PackInfo pack = new(symbol, attribute);
        if (symbol.TypeKind != TypeKind.Class
            || symbol.IsStatic
            || symbol.IsGenericType
            || symbol.ContainingType is not null)
        {
            Report(context, _invalidDeclaration, GetLocation(attribute, symbol), "Option packs must be non-generic, non-static, top-level classes.");
            pack.IsValid = false;
        }

        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ClassDeclarationSyntax declaration
                || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                Report(context, _invalidDeclaration, reference.GetSyntax().GetLocation(), "Option-pack declarations must be partial classes.");
                pack.IsValid = false;
            }
        }

        foreach (AttributeData optionAttribute in symbol.GetAttributes()
            .Where(static item => IsAttribute(item, _packOptionAttributeName)))
        {
            ITypeSymbol? valueType = GetAttributeTypeArgument(optionAttribute);
            if (valueType is null || !ValidatePackOption(context, optionAttribute, valueType))
            {
                pack.IsValid = false;
                continue;
            }

            pack.Options.Add(new PackOptionInfo(
                optionAttribute,
                valueType,
                GetSourceOrder(optionAttribute, symbol, syntaxTreeOrder)));
        }

        pack.Options.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));

        string[] fieldNames = [.. pack.Options.Select((_, index) => string.Concat("Option", index.ToString(CultureInfo.InvariantCulture)))];
        if (fieldNames.Any(fieldName => symbol.GetMembers(fieldName).Length > 0)
            || symbol.GetMembers("__MauveBuildPack_" + GetStableSuffix(symbol)).Length > 0)
        {
            Report(context, _invalidDeclaration, GetLocation(attribute, symbol), "A generated option-pack member conflicts with a declared member.");
            pack.IsValid = false;
        }

        return pack;
    }

    private static bool ValidatePackOption(
        SourceProductionContext context,
        AttributeData attribute,
        ITypeSymbol valueType)
    {
        string name = GetConstructorString(attribute, 0) ?? string.Empty;
        string[] spellings = [name, .. GetNamedStrings(attribute, "Aliases")];
        int arity = GetOptionArity(attribute, valueType);
        int bareValuePolicy = GetNamedInt(attribute, "BareValuePolicy");
        bool collection = arity is 3 or 4;
        bool scalar = arity is 0 or 1;
        bool typeMismatch = collection
            ? !TryGetCollectionElementType(valueType, out _)
            : scalar && TryGetCollectionElementType(valueType, out _);
        bool nullableType = IsNullable(valueType);
        bool canRepresentNull = nullableType || valueType.IsReferenceType;
        if (!IsValidLongOptionName(name)
            || GetNamedStrings(attribute, "Aliases").Any(static alias => !IsValidOptionAlias(alias))
            || spellings.Distinct(StringComparer.Ordinal).Count() != spellings.Length
            || arity is < 0 or > 4
            || typeMismatch
            || (arity == 2 && valueType.SpecialType != SpecialType.System_Boolean)
            || (GetNamedBoolean(attribute, "AllowsNull") && !canRepresentNull)
            || (arity == 1 && bareValuePolicy != 1)
            || (arity != 1 && bareValuePolicy != 0)
            || (bareValuePolicy == 1 && !GetNamedBoolean(attribute, "AllowsNull")))
        {
            Report(context, _invalidMember, GetLocation(attribute, valueType), "The pack option has an invalid name, arity, nullability, requiredness or value type.");
            return false;
        }

        return true;
    }

    private static void ReadPackInclusions(
        SourceProductionContext context,
        PackInfo pack,
        Dictionary<INamedTypeSymbol, PackInfo> packs,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder)
    {
        foreach (AttributeData attribute in pack.Symbol.GetAttributes()
            .Where(static item => IsAttribute(item, _packAttributeName)))
        {
            if (GetAttributeTypeArgument(attribute) is not INamedTypeSymbol packType
                || !packs.TryGetValue(packType, out PackInfo? child))
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, pack.Symbol), "Every nested pack type must have a CliOptionPack declaration in this compilation.");
                pack.IsValid = false;
                continue;
            }

            pack.Inclusions.Add(new PackInclusionInfo(
                pack.Symbol,
                child,
                GetConstructorString(attribute, 0) ?? string.Empty,
                GetSourceOrder(attribute, pack.Symbol, syntaxTreeOrder),
                GetNamedString(attribute, "Title"),
                GetNamedString(attribute, "Description"),
                attribute));
        }

        pack.Inclusions.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));
    }

    private static void ReadCommandPacks(
        SourceProductionContext context,
        CommandInfo command,
        Dictionary<INamedTypeSymbol, PackInfo> packs,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder)
    {
        foreach (AttributeData attribute in command.Symbol.GetAttributes()
            .Where(static item => IsAttribute(item, _packAttributeName)))
        {
            if (GetAttributeTypeArgument(attribute) is not INamedTypeSymbol packType
                || !packs.TryGetValue(packType, out PackInfo? pack))
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, command.Symbol), "Every included pack type must have a CliOptionPack declaration in this compilation.");
                command.IsValid = false;
                continue;
            }

            command.Packs.Add(new PackInclusionInfo(
                command.Symbol,
                pack,
                GetConstructorString(attribute, 0) ?? string.Empty,
                GetSourceOrder(attribute, command.Symbol, syntaxTreeOrder),
                GetNamedString(attribute, "Title"),
                GetNamedString(attribute, "Description"),
                attribute));
        }

        command.Packs.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));
    }

    private static bool ValidateProperty(
        SourceProductionContext context,
        IPropertySymbol property,
        AttributeData attribute)
    {
        bool isOption = IsAttribute(attribute, _optionAttributeName);
        string declaredName = GetConstructorString(attribute, 0)
            ?? (isOption ? DeriveOptionName(property.Name) : DeriveMemberName(property.Name));
        if (isOption)
        {
            string[] spellings = [declaredName, .. GetNamedStrings(attribute, "Aliases")];
            if (!IsValidLongOptionName(declaredName)
                || GetNamedStrings(attribute, "Aliases").Any(static alias => !IsValidOptionAlias(alias))
                || spellings.Distinct(StringComparer.Ordinal).Count() != spellings.Length)
            {
                Report(context, _invalidMember, GetLocation(attribute, property), "Option names and aliases must be valid and unique.");
                return false;
            }
        }
        else if (!IsValidCommandName(declaredName))
        {
            Report(context, _invalidMember, GetLocation(attribute, property), "Positional names must follow the supported command-name rules.");
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
            Report(context, _invalidMember, GetLocation(attribute, property), "A bound member must be a partial, get-only, non-static instance property with a supported value type.");
            return false;
        }

        if (property.Type.IsReferenceType && property.Type.NullableAnnotation == NullableAnnotation.None)
        {
            Report(context, _invalidMember, GetLocation(attribute, property), "Bound reference properties require an explicit nullable or nonnullable annotation.");
            return false;
        }

        if (GetNamedBoolean(attribute, "AllowsNull") && !IsNullable(property.Type))
        {
            Report(context, _invalidMember, GetLocation(attribute, property), "A member that allows null values must use a nullable property type.");
            return false;
        }

        if (isOption)
        {
            int arity = GetOptionArity(attribute, property.Type);
            int bareValuePolicy = GetNamedInt(attribute, "BareValuePolicy");
            bool collection = arity is 3 or 4;
            if ((collection && !TryGetCollectionElementType(property.Type, out _))
                || (!collection && arity is 0 or 1 && TryGetCollectionElementType(property.Type, out _))
                || (arity == 2 && property.Type.SpecialType != SpecialType.System_Boolean))
            {
                Report(context, _invalidMember, GetLocation(attribute, property), "Collection options require an IReadOnlyList<T> property, and boolean flags require a bool property.");
                return false;
            }

            if (arity is < 0 or > 4
                || GetNamedInt(attribute, "Scope") is < 0 or > 1
                || (arity == 1 && bareValuePolicy != 1)
                || (arity != 1 && bareValuePolicy != 0)
                || (bareValuePolicy == 1 && !IsNullable(property.Type))
                || (arity is 0 or 1 && !GetNamedBoolean(attribute, "IsRequired") && !IsNullable(property.Type)))
            {
                Report(context, _invalidMember, GetLocation(attribute, property), "Optional-valued options must use the supported null bare-value policy, and possibly absent scalar options require nullable properties.");
                return false;
            }
        }
        else
        {
            (int minimum, int maximum) = GetArgumentBounds(attribute, property.Type);
            bool isRest = maximum == -1;
            bool isScalar = maximum == 1;
            bool typeMismatch = isRest
                ? !TryGetCollectionElementType(property.Type, out _)
                : isScalar && TryGetCollectionElementType(property.Type, out _);
            if (minimum is < 0 or > 1 || (!isRest && !isScalar) || typeMismatch)
            {
                Report(context, _invalidMember, GetLocation(attribute, property), "Single-value arguments require a scalar property; multi-value arguments require an IReadOnlyList<T> property.");
                return false;
            }

            if (minimum == 0 && !IsNullable(property.Type) && isScalar)
            {
                Report(context, _invalidMember, GetLocation(attribute, property), "An optional single-value argument requires a nullable property type.");
                return false;
            }
        }

        string generatedTryName = string.Concat("TryGet", property.Name);
        if (property.ContainingType.GetMembers(generatedTryName).Length > 0)
        {
            Report(context, _invalidMember, GetLocation(attribute, property), $"Generated accessor '{generatedTryName}' conflicts with an existing member.");
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
                && (IsNamedType(handler.Parameters[0].Type, "System.Threading", "CancellationToken")
                    || IsNamedType(handler.Parameters[0].Type, "Mauve.Cli.Invocation", "CommandInvocationContext")));
        if (handler.IsStatic
            || handler.IsGenericMethod
            || (handler.IsAsync && handler.ReturnsVoid)
            || !supportedReturn
            || !supportedParameters
            || command.Symbol.IsAbstract
            || !command.Symbol.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0))
        {
            Report(context, _invalidMember, GetLocation(FindAttribute(handler, _handlerAttributeName), handler), "Handlers must be instance methods with no parameters or one CancellationToken or CommandInvocationContext, a supported return type and a parameterless command constructor.");
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
            || (type is INamedTypeSymbol named
                && named.TypeArguments.Length == 1
                && named.TypeArguments[0].SpecialType == SpecialType.System_Int32
                && named.OriginalDefinition.ToDisplayString() is "System.Threading.Tasks.Task<TResult>"
                    or "System.Threading.Tasks.ValueTask<TResult>");
    }

    private static void ReadChildren(
        SourceProductionContext context,
        CommandInfo command,
        Dictionary<INamedTypeSymbol, CommandInfo> commands,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder)
    {
        foreach (AttributeData attribute in command.Symbol.GetAttributes()
            .Where(static item => IsAttribute(item, _childAttributeName)))
        {
            if (GetAttributeTypeArgument(attribute) is not INamedTypeSymbol childType
                || !commands.TryGetValue(childType, out CommandInfo? child))
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, command.Symbol), "Every child type must have a CliCommand declaration in this compilation.");
                command.IsValid = false;
                continue;
            }

            if (child.IsRoot)
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, command.Symbol), "A model root cannot be included as a child.");
                command.IsValid = false;
                continue;
            }

            command.Children.Add(new ChildInfo(
                command,
                child,
                attribute,
                GetSourceOrder(attribute, command.Symbol, syntaxTreeOrder)));
        }

        command.Children.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));
    }

    private static bool ValidateTree(
        SourceProductionContext context,
        CommandInfo command,
        HashSet<INamedTypeSymbol> active,
        HashSet<string> inheritedNames,
        HashSet<INamedTypeSymbol> activePacks)
    {
        if (!active.Add(command.Symbol))
        {
            Report(context, _invalidHierarchy, GetLocation(command.Attribute, command.Symbol), "The command hierarchy contains a cycle.");
            return false;
        }

        bool valid = command.IsValid;
        HashSet<string> commandNames = [with(StringComparer.Ordinal)];
        foreach (ChildInfo child in command.Children)
        {
            if (!commandNames.Add(child.Child.Name))
            {
                Report(context, _invalidHierarchy, GetLocation(child.Attribute, command.Symbol), "Sibling command names or aliases must be unique.");
                valid = false;
            }

            foreach (string alias in child.Child.Aliases)
            {
                if (!commandNames.Add(alias))
                {
                    Report(context, _invalidHierarchy, GetLocation(child.Attribute, command.Symbol), "Sibling command names or aliases must be unique.");
                    valid = false;
                }
            }
        }

        HashSet<string> effective = new(inheritedNames, StringComparer.Ordinal);
        HashSet<string> inheritedForChildren = new(inheritedNames, StringComparer.Ordinal);
        foreach (OptionInfo option in command.Options)
        {
            string optionName = GetConstructorString(option.Attribute, 0)
                ?? DeriveOptionName(option.Property.Name);
            valid &= AddVisibleOption(
                context,
                option.Attribute,
                option.Property,
                optionName,
                GetNamedStrings(option.Attribute, "Aliases"),
                effective);
            if (GetNamedInt(option.Attribute, "Scope") == 1)
            {
                AddOptionSpellings(
                    optionName,
                    GetNamedStrings(option.Attribute, "Aliases"),
                    inheritedForChildren);
            }
        }

        valid &= ValidatePackInclusions(context, command.Packs, activePacks, effective, inheritedForChildren);

        foreach (ChildInfo child in command.Children)
        {
            valid &= ValidateTree(context, child.Child, active, inheritedForChildren, activePacks);
        }

        _ = active.Remove(command.Symbol);
        return valid;
    }

    private static bool ValidatePackInclusions(
        SourceProductionContext context,
        List<PackInclusionInfo> inclusions,
        HashSet<INamedTypeSymbol> active,
        HashSet<string> visibleNames,
        HashSet<string> inheritedNames)
    {
        HashSet<string> siblingKeys = [with(StringComparer.Ordinal)];
        bool valid = true;
        foreach (PackInclusionInfo inclusion in inclusions)
        {
            if (!IsValidPackKey(inclusion.Key) || !siblingKeys.Add(inclusion.Key))
            {
                Report(context, _invalidHierarchy, GetLocation(inclusion.Attribute, inclusion.ParentSymbol), "Pack inclusion keys must be valid and unique among siblings.");
                valid = false;
            }

            PackInfo pack = inclusion.Pack;
            valid &= pack.IsValid;
            if (!active.Add(pack.Symbol))
            {
                Report(context, _invalidHierarchy, GetLocation(inclusion.Attribute, inclusion.ParentSymbol), "The option-pack hierarchy contains a cycle.");
                valid = false;
                continue;
            }

            valid &= ValidatePackInclusions(context, pack.Inclusions, active, visibleNames, inheritedNames);
            valid &= ValidatePackOptions(context, pack.Options, pack.Symbol, visibleNames, inheritedNames);
            _ = active.Remove(pack.Symbol);
        }

        return valid;
    }

    private static bool ValidatePackOptions(
        SourceProductionContext context,
        List<PackOptionInfo> options,
        INamedTypeSymbol owner,
        HashSet<string> visibleNames,
        HashSet<string> inheritedNames)
    {
        bool valid = true;
        foreach (PackOptionInfo option in options)
        {
            string name = GetConstructorString(option.Attribute, 0) ?? string.Empty;
            string[] aliases = GetNamedStrings(option.Attribute, "Aliases");
            valid &= AddVisibleOption(context, option.Attribute, owner, name, aliases, visibleNames);
            if (GetNamedInt(option.Attribute, "Scope") == 1)
            {
                AddOptionSpellings(name, aliases, inheritedNames);
            }
        }

        return valid;
    }

    private static bool AddVisibleOption(
        SourceProductionContext context,
        AttributeData attribute,
        ISymbol owner,
        string name,
        string[] aliases,
        HashSet<string> visibleNames)
    {
        bool valid = true;
        if (!visibleNames.Add(name))
        {
            Report(context, _invalidHierarchy, GetLocation(attribute, owner), "Visible option names and aliases must be unique.");
            valid = false;
        }

        foreach (string alias in aliases)
        {
            if (!visibleNames.Add(alias))
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, owner), "Visible option names and aliases must be unique.");
                valid = false;
            }
        }

        return valid;
    }

    private static void AddOptionSpellings(string name, string[] aliases, HashSet<string> names)
    {
        _ = names.Add(name);
        foreach (string alias in aliases)
        {
            _ = names.Add(alias);
        }
    }

    private static bool IsValidPackKey(string key)
    {
        return key.Length is > 0 and <= 128
            && key.All(static character =>
                IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return character is (>= 'A' and <= 'Z')
            or (>= 'a' and <= 'z')
            or (>= '0' and <= '9');
    }

    private static string GenerateMembers(CommandInfo command)
    {
        StringBuilder builder = new();
        string namespaceName = command.Symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : command.Symbol.ContainingNamespace.ToDisplayString();
        string suffix = GetStableSuffix(command.Symbol);
        string typeName = EscapeIdentifier(command.Symbol.Name);

        _ = builder.AppendLine("#nullable enable");
        if (namespaceName.Length > 0)
        {
            _ = builder.Append("namespace ").Append(namespaceName).AppendLine();
            _ = builder.AppendLine("{");
        }

        _ = builder.Append(AccessibilityText(command.Symbol.DeclaredAccessibility))
            .Append(" partial class ").Append(typeName);
        if (command.IsRoot)
        {
            _ = builder.Append(" : global::Mauve.Cli.Invocation.IGeneratedCliCommand");
            if (IsSynchronousTree(command))
            {
                _ = builder.Append(", global::Mauve.Cli.Invocation.ISynchronousGeneratedCliCommand");
            }
        }

        _ = builder.AppendLine();
        _ = builder.AppendLine("{");

        for (int index = 0; index < command.Options.Count; index++)
        {
            OptionInfo option = command.Options[index];
            _ = builder.Append("    private static readonly global::Mauve.Cli.Definitions.OptionDefinition ")
                .Append(OptionInfo.FieldName(suffix, index)).Append(" = ")
                .Append(GenerateOption(option)).AppendLine(";");
        }

        for (int index = 0; index < command.Arguments.Count; index++)
        {
            ArgumentInfo argument = command.Arguments[index];
            _ = builder.Append("    private static readonly global::Mauve.Cli.Definitions.PositionalDefinition ")
                .Append(ArgumentInfo.FieldName(suffix, index)).Append(" = ")
                .Append(GenerateArgument(argument)).AppendLine(";");
        }

        string contextField = string.Concat("__mauveContext_", suffix);
        string attachName = string.Concat("__MauveAttach_", suffix);
        _ = builder.Append("    private ").Append(_contextType).Append("? ").Append(contextField).AppendLine(";");
        _ = builder.Append("    internal void ").Append(attachName).Append('(').Append(_contextType).AppendLine(" context)");
        _ = builder.AppendLine("    {");
        _ = builder.Append("        ").Append(contextField).AppendLine(" = context;");
        _ = builder.AppendLine("    }");

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
        _ = builder.Append("    internal static global::Mauve.Cli.Definitions.CommandBuilder ").Append(buildName).AppendLine("()");
        _ = builder.AppendLine("    {");
        _ = builder.Append("        global::Mauve.Cli.Definitions.CommandBuilder command = new(")
            .Append(Literal(command.Name)).AppendLine(")");
        _ = builder.AppendLine("        {");
        _ = builder.Append("            Description = ").Append(Literal(command.Description)).AppendLine(",");
        _ = builder.Append("            LongDescription = ").Append(Literal(command.LongDescription)).AppendLine(",");
        _ = builder.Append("            IsHidden = ").Append(command.IsHidden ? "true" : "false").AppendLine();
        _ = builder.AppendLine("        };");
        foreach (string alias in command.Aliases)
        {
            _ = builder.Append("        _ = command.AddAlias(").Append(Literal(alias)).AppendLine(");");
        }

        for (int index = 0; index < command.Options.Count; index++)
        {
            _ = builder.Append("        _ = command.AddOption(").Append(OptionInfo.FieldName(suffix, index)).AppendLine(");");
        }

        foreach (PackInclusionInfo inclusion in command.Packs)
        {
            EmitPackInclusion(builder, "command", inclusion);
        }

        for (int index = 0; index < command.Arguments.Count; index++)
        {
            _ = builder.Append("        _ = command.AddPositional(").Append(ArgumentInfo.FieldName(suffix, index)).AppendLine(");");
        }

        foreach (ChildInfo child in command.Children)
        {
            _ = builder.Append("        _ = command.AddChild(")
                .Append(child.Child.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append('.').Append(string.Concat("__MauveBuildCommand_", GetStableSuffix(child.Child.Symbol)))
                .AppendLine("());");
        }

        if (command.Handler is not null)
        {
            _ = builder.Append("        _ = command.SetValueTaskHandlerWithExitCode(__MauveInvoke_")
                .Append(suffix).AppendLine(");");
        }

        _ = builder.AppendLine("        return command;");
        _ = builder.AppendLine("    }");

        if (command.Handler is not null)
        {
            EmitHandler(builder, command, suffix, attachName);
        }

        if (command.IsRoot)
        {
            _ = builder.AppendLine("    /// <summary>Creates a mutable builder for the generated command model.</summary>");
            _ = builder.Append("    public static global::Mauve.Cli.Definitions.CommandBuilder CreateBuilder() => ")
                .Append(buildName).AppendLine("();");
            _ = builder.AppendLine("    /// <summary>Creates a validated model from the generated declarations.</summary>");
            _ = builder.AppendLine("    /// <param name=\"limits\">Optional model and parsing limits.</param>");
            _ = builder.AppendLine("    /// <param name=\"includeStandardHelp\">Whether to add the standard help option.</param>");
            _ = builder.AppendLine("    public static global::Mauve.Cli.Definitions.CommandModel CreateModel(");
            _ = builder.AppendLine("        global::Mauve.Cli.Definitions.CliLimits? limits = null,");
            _ = builder.AppendLine("        bool includeStandardHelp = false)");
            _ = builder.AppendLine("    {");
            _ = builder.Append("        return global::Mauve.Cli.Definitions.CommandModel.Create(")
                .AppendLine("CreateBuilder(), limits, includeStandardHelp);");
            _ = builder.AppendLine("    }");
        }

        _ = builder.AppendLine("}");
        if (namespaceName.Length > 0)
        {
            _ = builder.AppendLine("}");
        }

        return builder.ToString();
    }

    private static bool IsSynchronousTree(CommandInfo command)
    {
        return (command.Handler is null || command.Handler.ReturnType.ToDisplayString() is "void" or "int")
            && command.Children.All(static child => IsSynchronousTree(child.Child));
    }

    private static string GeneratePackMembers(PackInfo pack)
    {
        StringBuilder builder = new();
        string namespaceName = pack.Symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : pack.Symbol.ContainingNamespace.ToDisplayString();
        string suffix = GetStableSuffix(pack.Symbol);
        string typeName = EscapeIdentifier(pack.Symbol.Name);
        if (namespaceName.Length > 0)
        {
            _ = builder.Append("namespace ").Append(namespaceName).AppendLine();
            _ = builder.AppendLine("{");
        }

        _ = builder.Append(AccessibilityText(pack.Symbol.DeclaredAccessibility))
            .Append(" partial class ").Append(typeName).AppendLine();
        _ = builder.AppendLine("{");
        for (int index = 0; index < pack.Options.Count; index++)
        {
            PackOptionInfo option = pack.Options[index];
            _ = builder.Append("    /// <summary>Gets the definition handle for option ").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(".</summary>");
            _ = builder.Append("    public static readonly global::Mauve.Cli.Definitions.OptionDefinition Option")
                .Append(index.ToString(CultureInfo.InvariantCulture)).Append(" = ")
                .Append(GenerateOption(option.Attribute, option.ValueType)).AppendLine(";");
        }

        string buildName = string.Concat("__MauveBuildPack_", suffix);
        _ = builder.Append("    internal static global::Mauve.Cli.Definitions.OptionPackBuilder ").Append(buildName).AppendLine("()");
        _ = builder.AppendLine("    {");
        _ = builder.AppendLine("        global::Mauve.Cli.Definitions.OptionPackBuilder pack = new();");
        foreach (PackInclusionInfo inclusion in pack.Inclusions)
        {
            EmitPackInclusion(builder, "pack", inclusion);
        }

        for (int index = 0; index < pack.Options.Count; index++)
        {
            _ = builder.Append("        _ = pack.AddOption(Option")
                .Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        }

        _ = builder.AppendLine("        return pack;");
        _ = builder.AppendLine("    }");
        _ = builder.AppendLine("}");
        if (namespaceName.Length > 0)
        {
            _ = builder.AppendLine("}");
        }

        return builder.ToString();
    }

    private static void EmitPackInclusion(StringBuilder builder, string receiver, PackInclusionInfo inclusion)
    {
        string methodName = string.Concat("__MauveBuildPack_", GetStableSuffix(inclusion.Pack.Symbol));
        _ = builder.Append("        _ = ").Append(receiver).Append(".AddPack(")
            .Append(Literal(inclusion.Key)).Append(", ")
            .Append(inclusion.Pack.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Append('.').Append(methodName).Append("(), ")
            .Append(Literal(inclusion.Title)).Append(", ")
            .Append(Literal(inclusion.Description)).AppendLine(");");
    }

    private static string GenerateOption(OptionInfo option) => GenerateOption(option.Attribute, option.Property.Type, option.Property.Name);

    private static string GenerateOption(
        AttributeData attribute,
        ITypeSymbol valueType,
        string? inferredMemberName = null)
    {
        string name = GetConstructorString(attribute, 0)
            ?? (inferredMemberName is null ? string.Empty : DeriveOptionName(inferredMemberName));
        int arityValue = GetOptionArity(attribute, valueType);
        string arity = EnumName("OptionArity", arityValue);
        string typeForTypeof = FormatTypeForTypeof(valueType);
        string elementType = string.Empty;
        if (arityValue is 3 or 4
            && TryGetCollectionElementType(valueType, out ITypeSymbol? optionElement)
            && optionElement is not null)
        {
            elementType = string.Concat(", elementType: typeof(", FormatTypeForTypeof(optionElement), ")");
        }
        string aliases = string.Join(
            ", ",
            GetNamedStrings(attribute, "Aliases")
                .Select(alias => $"new global::Mauve.Cli.Definitions.OptionAlias({Literal(alias)})"));
        string barePolicy = GetNamedInt(attribute, "BareValuePolicy") == 1
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
            GetNamedBoolean(attribute, "AllowsNull") || IsNullable(valueType) ? "true" : "false",
            ", isRequired: ",
            GetNamedBoolean(attribute, "IsRequired") ? "true" : "false",
            ", description: ",
            Literal(GetNamedString(attribute, "Description")),
            ", valueLabel: ",
            Literal(GetNamedString(attribute, "ValueLabel")),
            ", isHidden: ",
            GetNamedBoolean(attribute, "IsHidden") ? "true" : "false",
            ", isSensitive: ",
            GetNamedBoolean(attribute, "IsSensitive") ? "true" : "false",
            ", scope: global::Mauve.Cli.Definitions.OptionScope.",
            EnumName("OptionScope", GetNamedInt(attribute, "Scope")),
            barePolicy,
            ")");
    }

    private static string GenerateArgument(ArgumentInfo argument)
    {
        string name = GetConstructorString(argument.Attribute, 0)
            ?? DeriveMemberName(argument.Property.Name);
        (int minimum, int maximum) = GetArgumentBounds(argument.Attribute, argument.Property.Type);
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
            minimum.ToString(CultureInfo.InvariantCulture),
            ", maximumValues: ",
            maximumText,
            element,
            ", allowsNull: ",
            GetNamedBoolean(argument.Attribute, "AllowsNull") || IsNullable(argument.Property.Type) ? "true" : "false",
            ", description: ",
            Literal(GetNamedString(argument.Attribute, "Description")),
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
        _ = builder.Append("    ").Append(accessibility).Append(" partial ").Append(propertyType).Append(' ')
            .Append(propertyName).AppendLine();
        _ = builder.AppendLine("    {");
        _ = builder.AppendLine("        get");
        _ = builder.AppendLine("        {");
        _ = builder.Append("            if (!").Append(tryName).AppendLine("(out var value))");
        _ = builder.AppendLine("            {");
        _ = builder.AppendLine("                throw new global::System.InvalidOperationException(\"The command value is unavailable.\");");
        _ = builder.AppendLine("            }");
        _ = builder.AppendLine("            return value;");
        _ = builder.AppendLine("        }");
        _ = builder.AppendLine("    }");
        _ = builder.Append("    ").Append(accessibility).Append(" bool ").Append(tryName)
            .Append("(out ").Append(propertyType).AppendLine(" value)");
        _ = builder.AppendLine("    {");
        _ = builder.Append("        global::Mauve.Cli.Invocation.CommandInvocationContext context = ")
            .Append(contextField)
            .AppendLine(" ?? throw new global::System.InvalidOperationException(\"Command binding is not attached.\");");
        _ = builder.Append("        return context.")
            .Append(isOption ? "TryGetOptionValue<" : "TryGetPositionalValue<")
            .Append(propertyType)
            .Append(">(").Append(definitionField).AppendLine(", out value!);");
        _ = builder.AppendLine("    }");
    }

    private static void EmitHandler(
        StringBuilder builder,
        CommandInfo command,
        string suffix,
        string attachName)
    {
        IMethodSymbol handler = command.Handler!;
        string handlerName = EscapeIdentifier(handler.Name);
        bool takesToken = handler.Parameters.Length == 1
            && IsNamedType(handler.Parameters[0].Type, "System.Threading", "CancellationToken");
        bool takesContext = handler.Parameters.Length == 1 && !takesToken;
        string arguments = takesToken ? "cancellationToken" : takesContext ? "context" : string.Empty;
        string invocation = string.Concat("instance.", handlerName, "(", arguments, ")");
        string fullyQualifiedTypeName = command.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string returnType = handler.ReturnType.ToDisplayString();
        _ = builder.Append("    private static ");
        bool isAsync = returnType.StartsWith("System.Threading.Tasks.", StringComparison.Ordinal);
        if (isAsync)
        {
            _ = builder.Append("async ");
        }

        _ = builder.Append("global::System.Threading.Tasks.ValueTask<int> __MauveInvoke_")
            .Append(suffix).Append('(').Append(_contextType).AppendLine(" context, global::System.Threading.CancellationToken cancellationToken)");
        _ = builder.AppendLine("    {");
        _ = builder.Append("        ").Append(fullyQualifiedTypeName).AppendLine(" instance = new();");
        _ = builder.Append("        instance.").Append(attachName).AppendLine("(context);");
        if (returnType == "void")
        {
            _ = builder.Append("        ").Append(invocation).AppendLine(";");
            _ = builder.AppendLine("        return new global::System.Threading.Tasks.ValueTask<int>(0);");
        }
        else if (returnType == "int")
        {
            _ = builder.Append("        return new global::System.Threading.Tasks.ValueTask<int>(").Append(invocation).AppendLine(");");
        }
        else if (returnType == "System.Threading.Tasks.Task")
        {
            _ = builder.Append("        global::System.Threading.Tasks.Task task = ").Append(invocation).AppendLine(";");
            _ = builder.AppendLine("        if (task is null) throw new global::System.InvalidOperationException(\"The asynchronous handler returned a null task.\");");
            _ = builder.AppendLine("        await task.ConfigureAwait(false);");
            _ = builder.AppendLine("        return 0;");
        }
        else if (returnType == "System.Threading.Tasks.Task<int>")
        {
            _ = builder.Append("        global::System.Threading.Tasks.Task<int> task = ").Append(invocation).AppendLine(";");
            _ = builder.AppendLine("        if (task is null) throw new global::System.InvalidOperationException(\"The asynchronous handler returned a null task.\");");
            _ = builder.AppendLine("        return await task.ConfigureAwait(false);");
        }
        else if (returnType == "System.Threading.Tasks.ValueTask")
        {
            _ = builder.Append("        await ").Append(invocation).AppendLine(".ConfigureAwait(false);");
            _ = builder.AppendLine("        return 0;");
        }
        else
        {
            _ = builder.Append("        return await ").Append(invocation).AppendLine(".ConfigureAwait(false);");
        }

        _ = builder.AppendLine("    }");
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
        return name.Length is > 0 and <= 128
            && IsAsciiAlphaNumeric(name[0])
            && name.Skip(1).All(static character => IsAsciiAlphaNumeric(character) || character == '-');
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

    private static bool IsAsciiAlphaNumeric(char character) => IsAsciiLetter(character) || character is >= '0' and <= '9';

    private static bool IsAsciiLetter(char character) => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

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

    private static bool IsNamedType(ITypeSymbol type, string namespaceName, string typeName)
    {
        return type is INamedTypeSymbol named
            && named.Name == typeName
            && named.ContainingNamespace.ToDisplayString() == namespaceName;
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

    private static string Literal(string? value) => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

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
            Accessibility.NotApplicable => throw new NotImplementedException(),
            Accessibility.Private => throw new NotImplementedException(),
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
            _ = name.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return string.Concat(name, "_", GetStableSuffix(symbol), suffix, ".g.cs");
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName) => symbol.GetAttributes().FirstOrDefault(attribute => IsAttribute(attribute, metadataName));

    private static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        INamedTypeSymbol? attributeClass = attribute.AttributeClass?.OriginalDefinition;
        if (attributeClass is null)
        {
            return false;
        }

        string namespaceName = attributeClass.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : attributeClass.ContainingNamespace.ToDisplayString();
        string fullName = namespaceName.Length == 0
            ? attributeClass.MetadataName
            : string.Concat(namespaceName, ".", attributeClass.MetadataName);
        return string.Equals(fullName, metadataName, StringComparison.Ordinal);
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

    private static ITypeSymbol? GetAttributeTypeArgument(AttributeData attribute)
    {
        return attribute.AttributeClass is { TypeArguments.Length: 1 } attributeClass
            ? attributeClass.TypeArguments[0]
            : null;
    }

    private static string? GetNamedString(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value as string;

    private static int GetNamedInt(AttributeData attribute, string name, int defaultValue = 0)
    {
        KeyValuePair<string, TypedConstant> value = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name);
        return value.Key is null
            ? defaultValue
            : Convert.ToInt32(value.Value.Value, CultureInfo.InvariantCulture);
    }

    private static bool HasNamedArgument(AttributeData attribute, string name) => attribute.NamedArguments.Any(argument => argument.Key == name);

    private static int GetOptionArity(AttributeData attribute, ITypeSymbol valueType)
    {
        return HasNamedArgument(attribute, "Arity")
            ? GetNamedInt(attribute, "Arity")
            : valueType.SpecialType == SpecialType.System_Boolean && !IsNullable(valueType)
                ? 2
                : TryGetCollectionElementType(valueType, out _) ? 4 : 0;
    }

    private static (int Minimum, int Maximum) GetArgumentBounds(
        AttributeData attribute,
        ITypeSymbol valueType)
    {
        bool collection = TryGetCollectionElementType(valueType, out _);
        return (
            GetNamedInt(attribute, "MinimumValues", 1),
            GetNamedInt(attribute, "MaximumValues", collection ? -1 : 1));
    }

    private static string DeriveCommandName(string typeName)
    {
        const string suffix = "Command";
        string name = typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - suffix.Length)
            : typeName;
        return DeriveMemberName(name);
    }

    private static string DeriveOptionName(string memberName) => string.Concat("--", DeriveMemberName(memberName));

    private static string DeriveMemberName(string identifier)
    {
        StringBuilder builder = new(identifier.Length + 4);
        for (int index = 0; index < identifier.Length; index++)
        {
            char character = identifier[index];
            if (character == '_')
            {
                _ = builder.Append('-');
                continue;
            }

            bool upper = char.IsUpper(character);
            if (upper
                && index > 0
                && identifier[index - 1] != '_'
                && (char.IsLower(identifier[index - 1])
                    || char.IsDigit(identifier[index - 1])
                    || (index + 1 < identifier.Length && char.IsLower(identifier[index + 1]))))
            {
                _ = builder.Append('-');
            }

            _ = builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static SourceOrder GetSourceOrder(
        AttributeData attribute,
        ISymbol fallback,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder)
    {
        SyntaxReference? syntaxReference = attribute.ApplicationSyntaxReference
            ?? fallback.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxReference is null)
        {
            return new SourceOrder(int.MaxValue, int.MaxValue);
        }

        int treeOrder = syntaxTreeOrder.TryGetValue(syntaxReference.SyntaxTree, out int value)
            ? value
            : int.MaxValue;
        return new SourceOrder(treeOrder, syntaxReference.Span.Start);
    }

    private static bool GetNamedBoolean(AttributeData attribute, string name) => attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value.Value is true;

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
        string message) => context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));

    private sealed class CommandInfo(
        INamedTypeSymbol symbol,
        AttributeData attribute,
        string name,
        bool isRoot,
        string? description,
        string? longDescription,
        bool isHidden,
        string[] aliases)
    {
        public INamedTypeSymbol Symbol { get; } = symbol;

        public AttributeData Attribute { get; } = attribute;

        public string Name { get; } = name;

        public bool IsRoot { get; } = isRoot;

        public string? Description { get; } = description;

        public string? LongDescription { get; } = longDescription;

        public bool IsHidden { get; } = isHidden;

        public string[] Aliases { get; } = aliases;

        public List<OptionInfo> Options { get; } = [];

        public List<ArgumentInfo> Arguments { get; } = [];

        public List<ChildInfo> Children { get; } = [];

        public List<PackInclusionInfo> Packs { get; } = [];

        public IMethodSymbol? Handler { get; set; }

        public bool IsValid { get; set; } = true;
    }

    private sealed class PackInfo(INamedTypeSymbol symbol, AttributeData attribute)
    {
        public INamedTypeSymbol Symbol { get; } = symbol;

        public AttributeData Attribute { get; } = attribute;

        public string Name => Symbol.Name;

        public List<PackOptionInfo> Options { get; } = [];

        public List<PackInclusionInfo> Inclusions { get; } = [];

        public bool IsValid { get; set; } = true;
    }

    private sealed class PackOptionInfo(
        AttributeData attribute,
        ITypeSymbol valueType,
        SourceOrder sourceOrder)
    {
        public AttributeData Attribute { get; } = attribute;

        public ITypeSymbol ValueType { get; } = valueType;

        public SourceOrder SourceOrder { get; } = sourceOrder;
    }

    private sealed class PackInclusionInfo(
        INamedTypeSymbol parentSymbol,
        PackInfo pack,
        string key,
        SourceOrder sourceOrder,
        string? title,
        string? description,
        AttributeData attribute)
    {
        public INamedTypeSymbol ParentSymbol { get; } = parentSymbol;

        public PackInfo Pack { get; } = pack;

        public string Key { get; } = key;

        public SourceOrder SourceOrder { get; } = sourceOrder;

        public string? Title { get; } = title;

        public string? Description { get; } = description;

        public AttributeData Attribute { get; } = attribute;
    }

    private sealed class OptionInfo(
        IPropertySymbol property,
        AttributeData attribute,
        SourceOrder sourceOrder)
    {
        public IPropertySymbol Property { get; } = property;

        public AttributeData Attribute { get; } = attribute;

        public SourceOrder SourceOrder { get; } = sourceOrder;

        public static string FieldName(string suffix, int index) => string.Concat("__mauveOption_", suffix, "_", index.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class ArgumentInfo(
        IPropertySymbol property,
        AttributeData attribute,
        SourceOrder sourceOrder)
    {
        public IPropertySymbol Property { get; } = property;

        public AttributeData Attribute { get; } = attribute;

        public SourceOrder SourceOrder { get; } = sourceOrder;

        public static string FieldName(string suffix, int index) => string.Concat("__mauveArgument_", suffix, "_", index.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class ChildInfo(
        CommandInfo parent,
        CommandInfo child,
        AttributeData attribute,
        SourceOrder sourceOrder)
    {
        public CommandInfo Parent { get; } = parent;

        public CommandInfo Child { get; } = child;

        public AttributeData Attribute { get; } = attribute;

        public SourceOrder SourceOrder { get; } = sourceOrder;

    }

    private readonly struct SourceOrder(int syntaxTreeOrder, int spanStart)
        : IComparable<SourceOrder>
    {
        private int SyntaxTreeOrder { get; } = syntaxTreeOrder;

        private int SpanStart { get; } = spanStart;

        public int CompareTo(SourceOrder other)
        {
            int treeComparison = SyntaxTreeOrder.CompareTo(other.SyntaxTreeOrder);
            return treeComparison != 0 ? treeComparison : SpanStart.CompareTo(other.SpanStart);
        }
    }
}
