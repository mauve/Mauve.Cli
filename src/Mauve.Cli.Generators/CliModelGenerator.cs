using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
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
    private const string _parentAttributeName = "Mauve.Cli.Declarations.CliParentAttribute`1";
    private const string _optionAttributeName = "Mauve.Cli.Declarations.CliOptionAttribute";
    private const string _argumentAttributeName = "Mauve.Cli.Declarations.CliArgumentAttribute";
    private const string _handlerAttributeName = "Mauve.Cli.Declarations.CliHandlerAttribute";
    private const string _directiveAttributeName = "Mauve.Cli.Declarations.CliDirectiveAttribute";
    private const string _directiveInclusionAttributeName = "Mauve.Cli.Declarations.CliDirectiveAttribute`1";
    private const string _optionPackAttributeName = "Mauve.Cli.Declarations.CliOptionPackAttribute";
    private const string _converterAttributeName = "Mauve.Cli.Declarations.CliConverterAttribute`1";
    private const string _contextType = "global::Mauve.Cli.Invocation.CommandInvocationContext";
    private const string _packContextType = "global::Mauve.Cli.Invocation.OptionPackContext";

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
        IncrementalValuesProvider<INamedTypeSymbol> directiveDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                _directiveAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol);
        context.RegisterSourceOutput(
            commandDeclarations.Collect()
                .Combine(packDeclarations.Collect())
                .Combine(directiveDeclarations.Collect())
                .Combine(context.CompilationProvider),
            static (productionContext, input) =>
                Generate(
                    productionContext,
                    input.Left.Left.Left,
                    input.Left.Left.Right,
                    input.Left.Right,
                    input.Right));
    }

    private static void Generate(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> declaredTypes,
        ImmutableArray<INamedTypeSymbol> declaredPackTypes,
        ImmutableArray<INamedTypeSymbol> declaredDirectiveTypes,
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
        Dictionary<INamedTypeSymbol, DirectiveInfo> directives = [with(SymbolEqualityComparer.Default)];
        foreach (INamedTypeSymbol? type in declaredPackTypes.Distinct(SymbolEqualityComparer.Default)
            .OrderBy(static symbol => symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty, StringComparer.Ordinal).Cast<INamedTypeSymbol?>())
        {
            if (type is not null)
            {
                AttributeData? attribute = FindAttribute(type, _optionPackAttributeName);
                if (attribute is not null)
                {
                    packs.Add(type, CreatePackInfo(context, type, attribute, syntaxTreeOrder, compilation));
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

            CommandInfo command = CreateCommandInfo(context, type, attribute, syntaxTreeOrder, compilation);
            commands.Add(type, command);
        }

        foreach (INamedTypeSymbol? type in declaredDirectiveTypes.Distinct(SymbolEqualityComparer.Default)
            .OrderBy(static symbol => symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty, StringComparer.Ordinal).Cast<INamedTypeSymbol?>())
        {
            if (type is not null)
            {
                AttributeData? attribute = FindAttribute(type, _directiveAttributeName);
                if (attribute is not null && GetConstructorString(attribute, 0) is null)
                {
                    directives.Add(type, CreateDirectiveInfo(context, type, attribute, syntaxTreeOrder, compilation));
                }
            }
        }

        foreach (PackInfo pack in packs.Values)
        {
            pack.IsValid &= ReadPackInclusions(context, pack.Symbol, pack.Inclusions, packs);
        }

        foreach (CommandInfo command in commands.Values.OrderBy(static item =>
            item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            ReadCommandEdges(context, command, commands, syntaxTreeOrder);
            command.IsValid &= ReadPackInclusions(context, command.Symbol, command.Packs, packs);
            ReadCommandDirectives(context, command, directives, syntaxTreeOrder);
        }

        ValidateCommandGraph(context, commands.Values);

        foreach (CommandInfo command in commands.Values)
        {
            if (!command.IsRoot && command.Directives.Count > 0)
            {
                Report(context, _invalidHierarchy, GetLocation(command.Directives[0].Directive?.Attribute
                    ?? command.Directives[0].MethodDirective!.Attribute, command.Symbol), "Directives may be declared only on a generated command root.");
                command.IsValid = false;
            }
        }

        List<CommandInfo> validRoots = [];
        HashSet<CommandInfo> validCommands = [];
        HashSet<DirectiveInfo> validDirectives = [];
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

            if (!ValidateCommandDirectives(context, root))
            {
                continue;
            }

            if (!ReadConversionRegistrations(context, root, compilation))
            {
                continue;
            }

            validRoots.Add(root);
            AddCommandTree(root, validCommands);
            foreach (DirectiveRegistrationInfo directive in root.Directives)
            {
                if (directive.Directive is not null)
                {
                    _ = validDirectives.Add(directive.Directive);
                }
            }
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

        foreach (DirectiveInfo directive in validDirectives.OrderBy(static item =>
            item.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            context.AddSource(
                GetHintName(directive.Symbol, ".Directive"),
                SourceText.From(GenerateDirectiveMembers(directive), Encoding.UTF8));
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
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder,
        Compilation compilation)
    {
        string name = GetConstructorString(attribute, 0) ?? DeriveCommandName(symbol.Name);
        bool isRoot = GetNamedBoolean(attribute, "IsRoot");
        CliXmlDocumentation documentation = ReadDocumentation(context, symbol, attribute, readExamples: true);
        CommandInfo command = new(
            symbol,
            attribute,
            name,
            isRoot,
            GetDisplayString(attribute, "Description", documentation.Summary),
            GetDisplayString(attribute, "LongDescription", documentation.Remarks),
            GetNamedBoolean(attribute, "IsHidden"),
            GetNamedStrings(attribute, "Aliases"))
        {
            IsValid = documentation.IsValid
        };
        command.Examples.AddRange(documentation.Examples);

        if (symbol.TypeKind != TypeKind.Class
            || symbol.IsStatic
            || symbol.IsGenericType
            || symbol.ContainingType is not null
            || symbol.IsAbstract)
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

            if (!ValidateProperty(context, property, optionAttribute ?? argumentAttribute!, compilation, out DefaultInfo? defaultInfo))
            {
                command.IsValid = false;
                continue;
            }

            SourceOrder sourceOrder = GetSourceOrder(
                optionAttribute ?? argumentAttribute!,
                property,
                syntaxTreeOrder);
            CliXmlDocumentation propertyDocumentation = ReadDocumentation(
                context, property, optionAttribute ?? argumentAttribute!, readExamples: false);
            command.IsValid &= propertyDocumentation.IsValid;
            if (optionAttribute is not null)
            {
                command.Options.Add(new OptionInfo(property, optionAttribute, sourceOrder,
                    GetDisplayString(optionAttribute, "Description", propertyDocumentation.Summary), defaultInfo));
            }
            else
            {
                command.Arguments.Add(new ArgumentInfo(property, argumentAttribute!, sourceOrder,
                    GetDisplayString(argumentAttribute!, "Description", propertyDocumentation.Summary), defaultInfo));
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
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder,
        Compilation compilation)
    {
        CliXmlDocumentation documentation = ReadDocumentation(context, symbol, attribute, readExamples: false);
        PackInfo pack = new(symbol, attribute,
            GetDisplayString(attribute, "Title", documentation.Summary),
            GetDisplayString(attribute, "Description", documentation.Remarks))
        {
            IsValid = documentation.IsValid
        };
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

        foreach (IPropertySymbol property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            AttributeData? optionAttribute = FindAttribute(property, _optionAttributeName);
            AttributeData? argumentAttribute = FindAttribute(property, _argumentAttributeName);
            if (argumentAttribute is not null)
            {
                Report(context, _invalidMember, GetLocation(argumentAttribute, property), "Option packs cannot declare positional arguments.");
                pack.IsValid = false;
                continue;
            }

            if (optionAttribute is null)
            {
                continue;
            }

            if (!ValidateProperty(context, property, optionAttribute, compilation, out DefaultInfo? defaultInfo))
            {
                pack.IsValid = false;
                continue;
            }

            CliXmlDocumentation propertyDocumentation = ReadDocumentation(context, property, optionAttribute, readExamples: false);
            pack.IsValid &= propertyDocumentation.IsValid;
            pack.Options.Add(new OptionInfo(
                property,
                optionAttribute,
                GetSourceOrder(optionAttribute, property, syntaxTreeOrder),
                GetDisplayString(optionAttribute, "Description", propertyDocumentation.Summary),
                defaultInfo));
        }

        pack.Options.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));

        string suffix = GetStableSuffix(symbol);
        string[] fieldNames = [.. pack.Options.Select((_, index) => OptionInfo.FieldName(suffix, index))];
        if (fieldNames.Any(fieldName => symbol.GetMembers(fieldName).Length > 0)
            || symbol.GetMembers("__MauveBuildPack_" + suffix).Length > 0
            || symbol.GetMembers("__MauveCreatePack_" + suffix).Length > 0)
        {
            Report(context, _invalidDeclaration, GetLocation(attribute, symbol), "A generated option-pack member conflicts with a declared member.");
            pack.IsValid = false;
        }

        return pack;
    }

    private static bool ReadPackInclusions(
        SourceProductionContext context,
        INamedTypeSymbol owner,
        List<PackInclusionInfo> inclusions,
        Dictionary<INamedTypeSymbol, PackInfo> packs)
    {
        ImmutableArray<IMethodSymbol> constructors = owner.InstanceConstructors;
        if (constructors.Length != 1 || owner.IsAbstract)
        {
            Report(context, _invalidMember, owner.Locations.FirstOrDefault() ?? Location.None,
                "Commands and option packs must be concrete and declare exactly one instance constructor.");
            return false;
        }

        bool valid = true;
        foreach (ISymbol member in owner.GetMembers())
        {
            if (member is IFieldSymbol { IsRequired: true }
                || (member is IPropertySymbol { IsRequired: true } requiredProperty
                    && FindAttribute(requiredProperty, _optionAttributeName) is null
                    && FindAttribute(requiredProperty, _argumentAttributeName) is null))
            {
                Report(context, _invalidMember, member.Locations.FirstOrDefault() ?? Location.None,
                    "Required command and option-pack members must be declared CLI properties.");
                valid = false;
            }
        }

        for (INamedTypeSymbol? ancestor = owner.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            if (ancestor.GetMembers().Any(static member =>
                member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                Report(context, _invalidMember, owner.Locations.FirstOrDefault() ?? Location.None,
                    "Commands and option packs cannot inherit required members.");
                valid = false;
                break;
            }
        }

        HashSet<INamedTypeSymbol> includedTypes = [with(SymbolEqualityComparer.Default)];
        foreach (IParameterSymbol parameter in constructors[0].Parameters)
        {
            if (parameter.RefKind != RefKind.None
                || parameter.IsOptional
                || parameter.IsParams
                || parameter.Type.NullableAnnotation == NullableAnnotation.Annotated
                || parameter.Type is not INamedTypeSymbol packType
                || !packs.TryGetValue(packType, out PackInfo? pack))
            {
                Report(context, _invalidMember, parameter.Locations.FirstOrDefault() ?? Location.None,
                    "Constructor parameters must be non-nullable, non-optional CliOptionPack types passed by value.");
                valid = false;
                continue;
            }

            if (!includedTypes.Add(packType))
            {
                Report(context, _invalidHierarchy, parameter.Locations.FirstOrDefault() ?? Location.None,
                    "A constructor cannot include the same option-pack type more than once.");
                valid = false;
                continue;
            }

            inclusions.Add(new PackInclusionInfo(pack, parameter));
        }

        return valid;
    }

    private static DirectiveInfo CreateDirectiveInfo(
        SourceProductionContext context,
        INamedTypeSymbol symbol,
        AttributeData attribute,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder,
        Compilation compilation)
    {
        string name = GetConstructorString(attribute, 0) ?? DeriveDirectiveName(symbol.Name);
        DirectiveInfo directive = new(symbol, attribute, name);
        if (symbol.TypeKind != TypeKind.Class
            || symbol.IsStatic
            || symbol.IsGenericType
            || symbol.ContainingType is not null
            || symbol.IsAbstract
            || !symbol.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0))
        {
            Report(context, _invalidDeclaration, GetLocation(attribute, symbol), "Directives must be concrete, non-generic, non-static, top-level classes with a parameterless constructor.");
            directive.IsValid = false;
        }

        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ClassDeclarationSyntax declaration
                || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                Report(context, _invalidDeclaration, reference.GetSyntax().GetLocation(), "Directive declarations must be partial classes.");
                directive.IsValid = false;
            }
        }

        if (!IsValidCommandName(name))
        {
            Report(context, _invalidDeclaration, GetLocation(attribute, symbol), "Directive names must follow the supported command-name rules.");
            directive.IsValid = false;
        }

        foreach (IPropertySymbol property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            AttributeData? optionAttribute = FindAttribute(property, _optionAttributeName);
            if (optionAttribute is null)
            {
                continue;
            }

            if (!ValidateProperty(context, property, optionAttribute, compilation, out _, isDirective: true))
            {
                directive.IsValid = false;
                continue;
            }

            directive.Values.Add(new OptionInfo(
                property,
                optionAttribute,
                GetSourceOrder(optionAttribute, property, syntaxTreeOrder)));
        }

        directive.Values.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));
        IMethodSymbol[] handlers = [.. symbol.GetMembers().OfType<IMethodSymbol>()
            .Where(static method => FindAttribute(method, _handlerAttributeName) is not null)];
        if (handlers.Length > 1)
        {
            Report(context, _invalidMember, GetLocation(FindAttribute(handlers[1], _handlerAttributeName), handlers[1]), "A directive can declare at most one CliHandler method.");
            directive.IsValid = false;
        }
        else if (handlers.Length == 1)
        {
            directive.Handler = handlers[0];
            if (!ValidateDirectiveHandler(context, handlers[0]))
            {
                directive.IsValid = false;
            }
        }
        else
        {
            Report(context, _invalidMember, GetLocation(attribute, symbol), "A standalone directive must declare one CliHandler method.");
            directive.IsValid = false;
        }

        return directive;
    }

    private static bool ValidateDirectiveHandler(
        SourceProductionContext context,
        IMethodSymbol handler)
    {
        bool supportedReturn = IsSupportedDirectiveReturnType(handler.ReturnType);
        bool supportedParameters = handler.Parameters.Length == 0
            || (handler.Parameters.Length == 1
                && handler.Parameters[0].RefKind == RefKind.None
                && (IsNamedType(handler.Parameters[0].Type, "System.Threading", "CancellationToken")
                    || IsNamedType(handler.Parameters[0].Type, "Mauve.Cli.Invocation", "CommandInvocationContext")));
        if (handler.IsStatic
            || handler.IsGenericMethod
            || (handler.IsAsync && handler.ReturnsVoid)
            || !supportedReturn
            || !supportedParameters)
        {
            Report(context, _invalidMember, GetLocation(FindAttribute(handler, _handlerAttributeName), handler), "Directive handlers must be instance methods with no parameters or one CancellationToken or CommandInvocationContext and a supported return type.");
            return false;
        }

        return true;
    }

    private static bool IsSupportedDirectiveReturnType(ITypeSymbol type)
    {
        string display = type.ToDisplayString();
        return IsSupportedReturnType(type)
            || display == "Mauve.Cli.Invocation.DirectiveResult"
            || (type is INamedTypeSymbol named
                && named.TypeArguments.Length == 1
                && named.TypeArguments[0].ToDisplayString() == "Mauve.Cli.Invocation.DirectiveResult"
                && named.OriginalDefinition.ToDisplayString() is "System.Threading.Tasks.Task<TResult>"
                    or "System.Threading.Tasks.ValueTask<TResult>");
    }

    private static void ReadCommandDirectives(
        SourceProductionContext context,
        CommandInfo command,
        Dictionary<INamedTypeSymbol, DirectiveInfo> directives,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder)
    {
        foreach (AttributeData attribute in command.Symbol.GetAttributes()
            .Where(static item => IsAttribute(item, _directiveInclusionAttributeName)))
        {
            if (GetAttributeTypeArgument(attribute) is not INamedTypeSymbol directiveType
                || !directives.TryGetValue(directiveType, out DirectiveInfo? directive))
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, command.Symbol), "Every included directive type must have a CliDirective declaration in this compilation.");
                command.IsValid = false;
                continue;
            }

            command.Directives.Add(new DirectiveRegistrationInfo(
                directive,
                null,
                GetSourceOrder(attribute, command.Symbol, syntaxTreeOrder)));
        }

        foreach (IMethodSymbol method in command.Symbol.GetMembers().OfType<IMethodSymbol>())
        {
            AttributeData? attribute = FindAttribute(method, _directiveAttributeName);
            if (attribute is null)
            {
                continue;
            }

            string? name = GetConstructorString(attribute, 0);
            if (name is null)
            {
                Report(context, _invalidMember, GetLocation(attribute, method), "A method directive requires an explicit name.");
                command.IsValid = false;
                continue;
            }

            MethodDirectiveInfo methodDirective = new(method, attribute, name);
            if (!IsValidCommandName(name)
                || method.GetAttributes().Any(item => IsAttribute(item, _handlerAttributeName))
                || !ValidateDirectiveMethod(context, methodDirective))
            {
                command.IsValid = false;
            }

            command.Directives.Add(new DirectiveRegistrationInfo(
                null,
                methodDirective,
                GetSourceOrder(attribute, method, syntaxTreeOrder)));
        }

        command.Directives.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));
    }

    private static bool ValidateDirectiveMethod(
        SourceProductionContext context,
        MethodDirectiveInfo methodDirective)
    {
        IMethodSymbol method = methodDirective.Method;
        bool supportedReturn = IsSupportedDirectiveReturnType(method.ReturnType);
        bool supportedParameters = method.Parameters.Length == 0
            || (method.Parameters.Length == 1
                && method.Parameters[0].RefKind == RefKind.None
                && (IsNamedType(method.Parameters[0].Type, "System.Threading", "CancellationToken")
                    || IsNamedType(method.Parameters[0].Type, "Mauve.Cli.Invocation", "CommandInvocationContext")));
        if (method.IsStatic
            || method.IsGenericMethod
            || (method.IsAsync && method.ReturnsVoid)
            || !supportedReturn)
        {
            Report(context, _invalidMember, GetLocation(methodDirective.Attribute, method), "Method directives must be instance methods with no parameters or one CancellationToken or CommandInvocationContext and a supported return type.");
            return false;
        }

        if (!supportedParameters)
        {
            Report(context, _invalidMember, GetLocation(methodDirective.Attribute, method), "Method directives must accept no parameter or one CancellationToken or CommandInvocationContext.");
            return false;
        }

        if (method.ContainingType.IsAbstract
            || !method.ContainingType.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0))
        {
            Report(context, _invalidMember, GetLocation(methodDirective.Attribute, method), "A method directive requires a concrete command root with a parameterless constructor.");
            return false;
        }

        if (method.ContainingType.GetMembers().Any(static member =>
            member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
        {
            Report(context, _invalidMember, GetLocation(methodDirective.Attribute, method), "A method directive cannot construct a root with required members before command input is validated.");
            return false;
        }

        return true;
    }

    private static bool ValidateCommandDirectives(SourceProductionContext context, CommandInfo root)
    {
        if (!root.IsRoot && root.Directives.Count > 0)
        {
            Report(context, _invalidHierarchy, GetLocation(root.Attribute, root.Symbol), "Directives may be included only on a generated command root.");
            return false;
        }

        HashSet<string> names = [with(StringComparer.Ordinal)];
        bool valid = true;
        foreach (DirectiveRegistrationInfo registration in root.Directives)
        {
            string name = registration.Directive?.Name ?? registration.MethodDirective!.Name;
            if (!names.Add(name))
            {
                Report(
                    context,
                    _invalidHierarchy,
                    GetLocation(registration.Directive?.Attribute ?? registration.MethodDirective!.Attribute, root.Symbol),
                    "Root directive names must be unique.");
                valid = false;
            }

            if (registration.Directive is DirectiveInfo directive && !directive.IsValid)
            {
                valid = false;
            }
        }

        return valid;
    }

    private static bool ValidateProperty(
        SourceProductionContext context,
        IPropertySymbol property,
        AttributeData attribute,
        Compilation compilation,
        out DefaultInfo? defaultInfo,
        bool isDirective = false)
    {
        defaultInfo = null;
        bool isOption = IsAttribute(attribute, _optionAttributeName);
        string? declaredOptionName = isOption ? GetOptionLongName(attribute, property.Name) : null;
        string declaredName = isOption ? declaredOptionName ?? string.Empty
            : GetConstructorString(attribute, 0) ?? DeriveMemberName(property.Name);
        if (isOption)
        {
            List<string> spellings = [];
            if (declaredOptionName is not null)
            {
                spellings.Add(declaredOptionName);
            }
            spellings.AddRange(GetNamedStrings(attribute, "Aliases"));
            char shortName = GetNamedChar(attribute, "ShortName");
            char[] shortAliases = GetNamedChars(attribute, "ShortAliases");
            if ((declaredOptionName is not null && !IsValidLongOptionName(declaredOptionName))
                || (declaredOptionName is null && shortName == '\0' && shortAliases.Length == 0)
                || GetNamedStrings(attribute, "Aliases").Any(static alias => !IsValidOptionAlias(alias))
                || spellings.Distinct(StringComparer.Ordinal).Count() != spellings.Count
                || (shortName != '\0' && !IsAsciiLetter(shortName))
                || shortAliases.Any(static alias => !IsAsciiLetter(alias))
                || shortAliases.Distinct().Count() != shortAliases.Length
                || (shortName != '\0' && shortAliases.Contains(shortName)))
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
        if ((isDirective
                ? !hasPartialDeclaration || property.SetMethod is not null
                : hasPartialDeclaration || property.SetMethod?.IsInitOnly != true)
            || hasUserImplementation
            || property.IsStatic
            || property.IsAbstract
            || property.IsExtern
            || property.IsIndexer
            || property.ReturnsByRef
            || property.ReturnsByRefReadonly
            || property.GetMethod is null
            || property.Type.TypeKind == TypeKind.Dynamic
            || property.Type.TypeKind == TypeKind.Pointer)
        {
            Report(context, _invalidMember, GetLocation(attribute, property), isDirective
                ? "A directive value must be a partial, get-only, non-static instance property with a supported value type."
                : "A bound member must be a non-static auto-property with get and init accessors and a supported value type.");
            return false;
        }

        if (!isDirective && !TryReadDefault(context, property, compilation, out defaultInfo))
        {
            return false;
        }

        if (property.Type.IsReferenceType && property.Type.NullableAnnotation == NullableAnnotation.None)
        {
            Report(context, _invalidMember, GetLocation(attribute, property), "Bound reference properties require an explicit nullable or nonnullable annotation.");
            return false;
        }

        if (GetNamedBoolean(attribute, "AllowsNull") && !IsNullable(GetScalarType(property.Type)))
        {
            Report(context, _invalidMember, GetLocation(attribute, property), "A member that allows null values must use a nullable property type.");
            return false;
        }

        if (isOption)
        {
            int arity = GetOptionArity(attribute, property.Type);
            int bareValuePolicy = GetNamedInt(attribute, "BareValuePolicy");
            bool collection = arity is 3 or 4;
            bool counting = arity == 5;
            if ((collection && !TryGetCollectionElementType(property.Type, out _))
                || (!collection && arity is 0 or 1 && TryGetCollectionElementType(property.Type, out _))
                || (arity == 2 && property.Type.SpecialType != SpecialType.System_Boolean)
                || (counting && property.Type.SpecialType != SpecialType.System_Int32
                    && !(property.Type is INamedTypeSymbol nullableType
                        && nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                        && nullableType.TypeArguments[0].SpecialType == SpecialType.System_Int32)))
            {
                Report(context, _invalidMember, GetLocation(attribute, property), "Collection options require an IReadOnlyList<T> property, and boolean flags require a bool property.");
                return false;
            }

            if (arity is < 0 or > 5
                || GetNamedInt(attribute, "Scope") is < 0 or > 1
                || (arity == 1 && bareValuePolicy != 1)
                || (arity != 1 && bareValuePolicy != 0)
                || (bareValuePolicy == 1 && !IsNullable(property.Type))
                || (arity is 0 or 1 && !(isDirective ? GetNamedBoolean(attribute, "IsRequired") : IsRequiredOption(attribute, property))
                    && !IsNullable(property.Type) && defaultInfo?.Value is null))
            {
                Report(context, _invalidMember, GetLocation(attribute, property), "Optional-valued options must use the supported null bare-value policy, and possibly absent scalar options require nullable properties.");
                return false;
            }
        }
        else
        {
            (int minimum, int maximum) = GetArgumentBounds(attribute, property);
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

            if (minimum == 0 && !IsNullable(property.Type) && isScalar && defaultInfo?.Value is null)
            {
                Report(context, _invalidMember, GetLocation(attribute, property), "An optional single-value argument requires a nullable property type.");
                return false;
            }
        }

        if (defaultInfo is not null
            && ((isOption && GetOptionArity(attribute, property.Type) is 2 or 3 or 4 or 5)
                || (!isOption && GetArgumentBounds(attribute, property).Maximum == -1)))
        {
            Report(context, _invalidMember, GetLocation(attribute, property), "Initializers are not supported for flags, counting options, collections, or rest arguments.");
            return false;
        }

        string generatedTryName = string.Concat("TryGet", property.Name);
        if (isDirective && property.ContainingType.GetMembers(generatedTryName).Length > 0)
        {
            Report(context, _invalidMember, GetLocation(attribute, property), $"Generated accessor '{generatedTryName}' conflicts with an existing member.");
            return false;
        }

        return true;
    }

    private static bool TryReadDefault(
        SourceProductionContext context,
        IPropertySymbol property,
        Compilation compilation,
        out DefaultInfo? defaultInfo)
    {
        defaultInfo = null;
        PropertyDeclarationSyntax? declaration = property.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault();
        ExpressionSyntax? expression = declaration?.Initializer?.Value;
        if (expression is null)
        {
            return true;
        }

        SemanticModel model = compilation.GetSemanticModel(expression.SyntaxTree);
        Optional<object?> constant = model.GetConstantValue(expression, context.CancellationToken);
        ITypeSymbol type = UnwrapNullable(property.Type);
        bool supportedType = constant.Value is null
            || type.TypeKind == TypeKind.Enum
            || type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Double
            || type.SpecialType == SpecialType.System_String;
        if (!constant.HasValue
            || !supportedType
            || !model.ClassifyConversion(expression, property.Type).IsImplicit
            || (constant.Value is null && !IsNullable(property.Type)))
        {
            Report(context, _invalidMember, expression.GetLocation(), "A bound initializer must be a compatible compile-time primitive, enum, string, or nullable null constant.");
            return false;
        }

        object? value = ConvertDefaultConstant(constant.Value, type);
        string valueExpression = value is null
            ? "null"
            : string.Concat("(", FormatType(property.Type), ")(", FormatConstant(value), ")");
        string display = FormatDefaultDisplay(value, type);
        defaultInfo = new DefaultInfo(value, valueExpression, display);
        return true;
    }

    private static object? ConvertDefaultConstant(object? value, ITypeSymbol type)
    {
        if (value is null)
        {
            return null;
        }

        ITypeSymbol storageType = type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: not null } named
            ? named.EnumUnderlyingType
            : type;
        TypeCode typeCode = (int)storageType.SpecialType switch
        {
            (int)SpecialType.System_Boolean => TypeCode.Boolean,
            (int)SpecialType.System_Char => TypeCode.Char,
            (int)SpecialType.System_SByte => TypeCode.SByte,
            (int)SpecialType.System_Byte => TypeCode.Byte,
            (int)SpecialType.System_Int16 => TypeCode.Int16,
            (int)SpecialType.System_UInt16 => TypeCode.UInt16,
            (int)SpecialType.System_Int32 => TypeCode.Int32,
            (int)SpecialType.System_UInt32 => TypeCode.UInt32,
            (int)SpecialType.System_Int64 => TypeCode.Int64,
            (int)SpecialType.System_UInt64 => TypeCode.UInt64,
            (int)SpecialType.System_Single => TypeCode.Single,
            (int)SpecialType.System_Double => TypeCode.Double,
            (int)SpecialType.System_Decimal => TypeCode.Decimal,
            (int)SpecialType.System_String => TypeCode.String,
            _ => throw new InvalidOperationException("The initializer constant has an unsupported type.")
        };
        object convertedValue = value is char character && typeCode is not TypeCode.Char and not TypeCode.String
            ? (ushort)character
            : value;
        return Convert.ChangeType(convertedValue, typeCode, CultureInfo.InvariantCulture);
    }

    private static string FormatConstant(object value)
    {
        return value switch
        {
            decimal decimalValue => string.Concat(decimalValue.ToString(CultureInfo.InvariantCulture), "m"),
            float singleValue => float.IsNaN(singleValue) ? "global::System.Single.NaN"
                : float.IsPositiveInfinity(singleValue) ? "global::System.Single.PositiveInfinity"
                : float.IsNegativeInfinity(singleValue) ? "global::System.Single.NegativeInfinity"
                : string.Concat(singleValue.ToString("R", CultureInfo.InvariantCulture), "f"),
            double doubleValue => double.IsNaN(doubleValue) ? "global::System.Double.NaN"
                : double.IsPositiveInfinity(doubleValue) ? "global::System.Double.PositiveInfinity"
                : double.IsNegativeInfinity(doubleValue) ? "global::System.Double.NegativeInfinity"
                : string.Concat(doubleValue.ToString("R", CultureInfo.InvariantCulture), "d"),
            _ => SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)
                ?? throw new InvalidOperationException("The initializer constant cannot be formatted.")
        };
    }

    private static string FormatDefaultDisplay(object? value, ITypeSymbol type)
    {
        if (value is null)
        {
            return "null";
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            IFieldSymbol? field = enumType.GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(member => member.HasConstantValue && Equals(member.ConstantValue, value));
            if (field is not null)
            {
                return field.Name;
            }
        }

        return value is bool boolean
            ? boolean ? "true" : "false"
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
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
            || command.Symbol.IsAbstract)
        {
            Report(context, _invalidMember, GetLocation(FindAttribute(handler, _handlerAttributeName), handler), "Handlers must be instance methods with no parameters or one CancellationToken or CommandInvocationContext and a supported return type.");
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

    private static void ReadCommandEdges(
        SourceProductionContext context,
        CommandInfo command,
        Dictionary<INamedTypeSymbol, CommandInfo> commands,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrder)
    {
        foreach (AttributeData attribute in command.Symbol.GetAttributes()
            .Where(static item => IsAttribute(item, _childAttributeName) || IsAttribute(item, _parentAttributeName)))
        {
            bool isParentDeclaration = IsAttribute(attribute, _parentAttributeName);
            if (GetAttributeTypeArgument(attribute) is not INamedTypeSymbol referencedType
                || !commands.TryGetValue(referencedType, out CommandInfo? referencedCommand))
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, command.Symbol), isParentDeclaration
                    ? "Every parent type must have a CliCommand declaration in this compilation."
                    : "Every child type must have a CliCommand declaration in this compilation.");
                command.IsValid = false;
                continue;
            }

            CommandInfo parent = isParentDeclaration ? referencedCommand : command;
            CommandInfo child = isParentDeclaration ? command : referencedCommand;
            ChildInfo edge = new(
                parent,
                child,
                attribute,
                GetSourceOrder(attribute, command.Symbol, syntaxTreeOrder),
                isParentDeclaration);
            if (child.IsRoot)
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, command.Symbol), "A model root cannot be included as a child.");
                command.IsValid = false;
                edge.IsValid = false;
            }

            parent.Children.Add(edge);
        }
    }

    private static void ValidateCommandGraph(SourceProductionContext context, IEnumerable<CommandInfo> commands)
    {
        CommandInfo[] ordered = [.. commands.OrderBy(static command =>
            command.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)];
        foreach (CommandInfo command in ordered)
        {
            command.Children.Sort(static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));
            foreach (IGrouping<CommandInfo, ChildInfo> edges in command.Children.GroupBy(static edge => edge.Child))
            {
                if (!edges.Any(static edge => edge.IsParentDeclaration)
                    || !edges.Any(static edge => !edge.IsParentDeclaration))
                {
                    continue;
                }

                foreach (ChildInfo edge in edges)
                {
                    Report(context, _invalidHierarchy, GetLocation(edge.Attribute, edge.DeclaringCommand.Symbol),
                        "Declare each command relationship using either CliChild or CliParent, not both.");
                    edge.IsValid = false;
                }
            }
        }

        HashSet<CommandInfo> visited = [];
        HashSet<CommandInfo> active = [];
        foreach (CommandInfo command in ordered)
        {
            if (!visited.Add(command))
            {
                continue;
            }

            _ = active.Add(command);
            Stack<(CommandInfo Command, int NextChild)> stack = new();
            stack.Push((command, 0));
            while (stack.Count > 0)
            {
                (CommandInfo current, int nextChild) = stack.Pop();
                if (nextChild == current.Children.Count)
                {
                    _ = active.Remove(current);
                    continue;
                }

                stack.Push((current, nextChild + 1));
                ChildInfo edge = current.Children[nextChild];
                if (active.Contains(edge.Child))
                {
                    Report(context, _invalidHierarchy, GetLocation(edge.Attribute, edge.DeclaringCommand.Symbol),
                        "The command hierarchy contains a cycle.");
                    edge.IsValid = false;
                }
                else if (visited.Add(edge.Child))
                {
                    _ = active.Add(edge.Child);
                    stack.Push((edge.Child, 0));
                }
            }
        }
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
            if (!child.IsValid)
            {
                valid = false;
                continue;
            }

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
            string optionName = GetOptionLongName(option.Attribute, option.Property.Name) ?? string.Empty;
            valid &= AddVisibleOption(
                context,
                option.Attribute,
                option.Property,
                optionName,
                GetNamedStrings(option.Attribute, "Aliases"),
                GetNamedChar(option.Attribute, "ShortName"),
                GetNamedChars(option.Attribute, "ShortAliases"),
                effective);
            if (GetNamedInt(option.Attribute, "Scope") == 1)
            {
                AddOptionSpellings(
                    optionName,
                    GetNamedStrings(option.Attribute, "Aliases"),
                    GetNamedChar(option.Attribute, "ShortName"),
                    GetNamedChars(option.Attribute, "ShortAliases"),
                    inheritedForChildren);
            }
        }

        valid &= ValidatePackInclusions(context, command.Packs, activePacks, effective, inheritedForChildren);

        foreach (ChildInfo child in command.Children)
        {
            if (child.IsValid)
            {
                valid &= ValidateTree(context, child.Child, active, inheritedForChildren, activePacks);
            }
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
                Report(context, _invalidHierarchy, inclusion.Parameter.Locations.FirstOrDefault() ?? Location.None, "Generated pack identities must be unique among siblings.");
                valid = false;
            }

            PackInfo pack = inclusion.Pack;
            valid &= pack.IsValid;
            if (!active.Add(pack.Symbol))
            {
                Report(context, _invalidHierarchy, inclusion.Parameter.Locations.FirstOrDefault() ?? Location.None, "The option-pack hierarchy contains a cycle.");
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
        List<OptionInfo> options,
        INamedTypeSymbol owner,
        HashSet<string> visibleNames,
        HashSet<string> inheritedNames)
    {
        bool valid = true;
        foreach (OptionInfo option in options)
        {
            string name = GetOptionLongName(option.Attribute, option.Property.Name) ?? string.Empty;
            string[] aliases = GetNamedStrings(option.Attribute, "Aliases");
            char shortName = GetNamedChar(option.Attribute, "ShortName");
            char[] shortAliases = GetNamedChars(option.Attribute, "ShortAliases");
            valid &= AddVisibleOption(context, option.Attribute, owner, name, aliases, shortName, shortAliases, visibleNames);
            if (GetNamedInt(option.Attribute, "Scope") == 1)
            {
                AddOptionSpellings(name, aliases, shortName, shortAliases, inheritedNames);
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
        char shortName,
        char[] shortAliases,
        HashSet<string> visibleNames)
    {
        bool valid = true;
        if (name.Length > 0 && !visibleNames.Add(OptionNameKey(name)))
        {
            Report(context, _invalidHierarchy, GetLocation(attribute, owner), "Visible option names and aliases must be unique.");
            valid = false;
        }

        foreach (string alias in aliases)
        {
            if (!visibleNames.Add(OptionNameKey(alias)))
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, owner), "Visible option names and aliases must be unique.");
                valid = false;
            }
        }

        if (shortName != '\0' && !visibleNames.Add(ShortNameKey(shortName)))
        {
            Report(context, _invalidHierarchy, GetLocation(attribute, owner), "Visible option names and aliases must be unique.");
            valid = false;
        }

        foreach (char alias in shortAliases)
        {
            if (!visibleNames.Add(ShortNameKey(alias)))
            {
                Report(context, _invalidHierarchy, GetLocation(attribute, owner), "Visible option names and aliases must be unique.");
                valid = false;
            }
        }

        return valid;
    }

    private static void AddOptionSpellings(string name, string[] aliases, char shortName, char[] shortAliases, HashSet<string> names)
    {
        if (name.Length > 0)
        {
            _ = names.Add(OptionNameKey(name));
        }

        foreach (string alias in aliases)
        {
            _ = names.Add(OptionNameKey(alias));
        }

        if (shortName != '\0')
        {
            _ = names.Add(ShortNameKey(shortName));
        }

        foreach (char shortAlias in shortAliases)
        {
            _ = names.Add(ShortNameKey(shortAlias));
        }
    }

    private static string OptionNameKey(string name) =>
        name.Length == 2 && name[0] == '-' && IsAsciiLetter(name[1])
            ? ShortNameKey(name[1])
            : string.Concat("L:", name.StartsWith("--", StringComparison.Ordinal) ? name.Substring(2) : name);

    private static string ShortNameKey(char name) => string.Concat("S:", name);

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
            _ = builder.Append(" : global::Mauve.Cli.Invocation.IGeneratedCliCommandBuilder");
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

        foreach (string example in command.Examples)
        {
            _ = builder.Append("        _ = command.AddExample(").Append(Literal(example)).AppendLine(");");
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

        if (command.IsRoot)
        {
            EmitConversionRegistrations(builder, command);
            for (int index = 0; index < command.Directives.Count; index++)
            {
                EmitDirectiveRegistration(builder, command.Directives[index], index);
            }
        }

        _ = builder.AppendLine("        return command;");
        _ = builder.AppendLine("    }");

        if (command.Handler is not null)
        {
            EmitHandler(builder, command, suffix);
        }

        if (command.IsRoot)
        {
            for (int index = 0; index < command.Directives.Count; index++)
            {
                if (command.Directives[index].MethodDirective is MethodDirectiveInfo methodDirective)
                {
                    EmitMethodDirectiveInvoker(builder, command, methodDirective, index);
                }
            }
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

    private static string GenerateDirectiveMembers(DirectiveInfo directive)
    {
        StringBuilder builder = new();
        string namespaceName = directive.Symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : directive.Symbol.ContainingNamespace.ToDisplayString();
        string suffix = GetStableSuffix(directive.Symbol);
        string typeName = EscapeIdentifier(directive.Symbol.Name);
        string contextField = string.Concat("__mauveDirectiveContext_", suffix);
        string attachName = string.Concat("__MauveAttachDirective_", suffix);
        string buildName = string.Concat("__MauveBuildDirective_", suffix);

        _ = builder.AppendLine("#nullable enable");
        if (namespaceName.Length > 0)
        {
            _ = builder.Append("namespace ").Append(namespaceName).AppendLine();
            _ = builder.AppendLine("{");
        }

        _ = builder.Append(AccessibilityText(directive.Symbol.DeclaredAccessibility))
            .Append(" partial class ").Append(typeName).AppendLine();
        _ = builder.AppendLine("{");
        for (int index = 0; index < directive.Values.Count; index++)
        {
            OptionInfo value = directive.Values[index];
            string valueName = DeriveMemberName(value.Property.Name);
            bool isRequired = !HasNamedArgument(value.Attribute, "IsRequired")
                || GetNamedBoolean(value.Attribute, "IsRequired");
            _ = builder.Append("    private static readonly global::Mauve.Cli.Definitions.DirectiveValueDefinition ")
                .Append(DirectiveInfo.FieldName(suffix, index)).Append(" = new(")
                .Append(Literal(valueName)).Append(", typeof(").Append(FormatTypeForTypeof(value.Property.Type)).Append("), ")
                .Append("isRequired: ").Append(isRequired ? "true" : "false")
                .Append(", description: ").Append(Literal(GetNamedString(value.Attribute, "Description")))
                .Append(", isHidden: ").Append(GetNamedBoolean(value.Attribute, "IsHidden") ? "true" : "false")
                .Append(", isSensitive: ").Append(GetNamedBoolean(value.Attribute, "IsSensitive") ? "true" : "false")
                .Append(GeneratePropertyConverter(value.Property))
                .AppendLine(");");
        }

        _ = builder.Append("    private global::Mauve.Cli.Invocation.DirectiveInvocationContext? ")
            .Append(contextField).AppendLine(";");
        _ = builder.Append("    internal void ").Append(attachName)
            .AppendLine("(global::Mauve.Cli.Invocation.DirectiveInvocationContext context)");
        _ = builder.AppendLine("    {");
        _ = builder.Append("        ").Append(contextField).AppendLine(" = context;");
        _ = builder.AppendLine("    }");

        for (int index = 0; index < directive.Values.Count; index++)
        {
            EmitDirectivePropertyBinding(
                builder,
                directive.Values[index].Property,
                DirectiveInfo.FieldName(suffix, index),
                contextField);
        }

        _ = builder.Append("    internal static global::Mauve.Cli.Definitions.DirectiveBuilder ")
            .Append(buildName).AppendLine("()");
        _ = builder.AppendLine("    {");
        _ = builder.Append("        global::Mauve.Cli.Definitions.DirectiveBuilder directive = new(")
            .Append(Literal(directive.Name)).AppendLine(")");
        _ = builder.AppendLine("        {");
        _ = builder.Append("            Description = ").Append(Literal(directive.Description)).AppendLine(",");
        _ = builder.Append("            IsHidden = ").Append(directive.IsHidden ? "true" : "false").AppendLine();
        _ = builder.AppendLine("        };");
        for (int index = 0; index < directive.Values.Count; index++)
        {
            _ = builder.Append("        _ = directive.AddValue(")
                .Append(DirectiveInfo.FieldName(suffix, index)).AppendLine(");");
        }

        _ = builder.Append("        _ = directive.SetValueTaskHandler(__MauveInvokeDirective_")
            .Append(suffix).AppendLine(");");
        _ = builder.AppendLine("        return directive;");
        _ = builder.AppendLine("    }");
        EmitStandaloneDirectiveInvoker(builder, directive, suffix, attachName);
        _ = builder.AppendLine("}");
        if (namespaceName.Length > 0)
        {
            _ = builder.AppendLine("}");
        }

        return builder.ToString();
    }

    private static void EmitDirectivePropertyBinding(
        StringBuilder builder,
        IPropertySymbol property,
        string definitionField,
        string contextField)
    {
        string propertyType = FormatType(property.Type);
        string accessibility = AccessibilityText(property.DeclaredAccessibility);
        string tryName = string.Concat("TryGet", property.Name);
        _ = builder.Append("    ").Append(accessibility).Append(" partial ").Append(propertyType).Append(' ')
            .Append(EscapeIdentifier(property.Name)).AppendLine();
        _ = builder.AppendLine("    {");
        _ = builder.AppendLine("        get");
        _ = builder.AppendLine("        {");
        _ = builder.Append("            if (!").Append(tryName).AppendLine("(out var value))");
        _ = builder.AppendLine("            {");
        _ = builder.AppendLine("                throw new global::System.InvalidOperationException(\"The directive value is unavailable.\");");
        _ = builder.AppendLine("            }");
        _ = builder.AppendLine("            return value;");
        _ = builder.AppendLine("        }");
        _ = builder.AppendLine("    }");
        _ = builder.Append("    ").Append(accessibility).Append(" bool ").Append(tryName)
            .Append("(out ").Append(propertyType).AppendLine(" value)");
        _ = builder.AppendLine("    {");
        _ = builder.Append("        global::Mauve.Cli.Invocation.DirectiveInvocationContext context = ")
            .Append(contextField)
            .AppendLine(" ?? throw new global::System.InvalidOperationException(\"Directive binding is not attached.\");");
        _ = builder.Append("        return context.TryGetValue<").Append(propertyType)
            .Append(">(").Append(definitionField).AppendLine(", out value!);");
        _ = builder.AppendLine("    }");
    }

    private static void EmitStandaloneDirectiveInvoker(
        StringBuilder builder,
        DirectiveInfo directive,
        string suffix,
        string attachName)
    {
        IMethodSymbol handler = directive.Handler!;
        string returnType = handler.ReturnType.ToDisplayString();
        string typeName = directive.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string methodName = EscapeIdentifier(handler.Name);
        string arguments = handler.Parameters.Length == 0
            ? string.Empty
            : IsNamedType(handler.Parameters[0].Type, "System.Threading", "CancellationToken")
                ? "cancellationToken"
                : "context.CommandContext";
        string invocation = string.Concat("instance.", methodName, "(", arguments, ")");
        _ = builder.Append("    private static async global::System.Threading.Tasks.ValueTask<global::Mauve.Cli.Invocation.DirectiveResult> __MauveInvokeDirective_")
            .Append(suffix).AppendLine("(global::Mauve.Cli.Invocation.DirectiveInvocationContext context, global::System.Threading.CancellationToken cancellationToken)");
        _ = builder.AppendLine("    {");
        _ = builder.AppendLine("        await global::System.Threading.Tasks.ValueTask.CompletedTask;");
        _ = builder.Append("        ").Append(typeName).AppendLine(" instance = new();");
        _ = builder.Append("        instance.").Append(attachName).AppendLine("(context);");
        if (returnType == "void")
        {
            _ = builder.Append("        ").Append(invocation).AppendLine(";");
            _ = builder.AppendLine("        return global::Mauve.Cli.Invocation.DirectiveResult.ContinueProcessing;");
        }
        else if (returnType == "int")
        {
            _ = builder.Append("        int result = ").Append(invocation).AppendLine(";");
            EmitIntDirectiveResult(builder);
        }
        else if (returnType == "Mauve.Cli.Invocation.DirectiveResult")
        {
            _ = builder.Append("        return ").Append(invocation).AppendLine(";");
        }
        else if (returnType == "System.Threading.Tasks.Task")
        {
            _ = builder.Append("        global::System.Threading.Tasks.Task task = ").Append(invocation).AppendLine(";");
            _ = builder.AppendLine("        if (task is null) throw new global::System.InvalidOperationException(\"The asynchronous directive returned a null task.\");");
            _ = builder.AppendLine("        await task.ConfigureAwait(false);");
            _ = builder.AppendLine("        return global::Mauve.Cli.Invocation.DirectiveResult.ContinueProcessing;");
        }
        else if (returnType == "System.Threading.Tasks.ValueTask")
        {
            _ = builder.Append("        await ").Append(invocation).AppendLine(".ConfigureAwait(false);");
            _ = builder.AppendLine("        return global::Mauve.Cli.Invocation.DirectiveResult.ContinueProcessing;");
        }
        else
        {
            bool resultIsDirective = returnType.EndsWith("<Mauve.Cli.Invocation.DirectiveResult>", StringComparison.Ordinal);
            string resultType = resultIsDirective ? "global::Mauve.Cli.Invocation.DirectiveResult" : "int";
            if (returnType.StartsWith("System.Threading.Tasks.Task<", StringComparison.Ordinal))
            {
                _ = builder.Append("        global::System.Threading.Tasks.Task<").Append(resultType).Append("> task = ")
                    .Append(invocation).AppendLine(";");
                _ = builder.AppendLine("        if (task is null) throw new global::System.InvalidOperationException(\"The asynchronous directive returned a null task.\");");
                _ = builder.Append("        ").Append(resultType).Append(" result = await task.ConfigureAwait(false);").AppendLine();
            }
            else
            {
                _ = builder.Append("        ").Append(resultType).Append(" result = await ")
                    .Append(invocation).AppendLine(".ConfigureAwait(false);");
            }

            if (resultIsDirective)
            {
                _ = builder.AppendLine("        return result;");
            }
            else
            {
                EmitIntDirectiveResult(builder);
            }
        }

        _ = builder.AppendLine("    }");
    }

    private static bool IsSynchronousTree(CommandInfo command)
    {
        return (command.Handler is null || command.Handler.ReturnType.ToDisplayString() is "void" or "int")
            && command.Directives.All(static directive =>
                directive.Directive is not null
                    ? directive.Directive.Handler is null
                        || directive.Directive.Handler.ReturnType.ToDisplayString() is "void" or "int"
                    : directive.MethodDirective!.Method.ReturnType.ToDisplayString() is "void" or "int"
                        or "Mauve.Cli.Invocation.DirectiveResult")
            && command.Children.All(static child => IsSynchronousTree(child.Child));
    }

    private static void EmitDirectiveRegistration(
        StringBuilder builder,
        DirectiveRegistrationInfo registration,
        int index)
    {
        if (registration.Directive is DirectiveInfo directive)
        {
            _ = builder.Append("        _ = command.AddDirective(")
                .Append(directive.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append('.').Append(string.Concat("__MauveBuildDirective_", GetStableSuffix(directive.Symbol)))
                .AppendLine("());");
            return;
        }

        MethodDirectiveInfo methodDirective = registration.MethodDirective!;
        string localName = string.Concat("directive_", index.ToString(CultureInfo.InvariantCulture));
        string helperName = string.Concat("__MauveInvokeDirective_", index.ToString(CultureInfo.InvariantCulture));
        _ = builder.Append("        global::Mauve.Cli.Definitions.DirectiveBuilder ").Append(localName)
            .Append(" = new(").Append(Literal(methodDirective.Name)).AppendLine(")");
        _ = builder.AppendLine("        {");
        _ = builder.Append("            Description = ").Append(Literal(GetNamedString(methodDirective.Attribute, "Description"))).AppendLine(",");
        _ = builder.Append("            IsHidden = ").Append(GetNamedBoolean(methodDirective.Attribute, "IsHidden") ? "true" : "false").AppendLine();
        _ = builder.AppendLine("        };");
        _ = builder.Append("        _ = ").Append(localName).Append(".SetValueTaskHandler(").Append(helperName).AppendLine(");");
        _ = builder.Append("        _ = command.AddDirective(").Append(localName).AppendLine(");");
    }

    private static void EmitMethodDirectiveInvoker(
        StringBuilder builder,
        CommandInfo command,
        MethodDirectiveInfo methodDirective,
        int index)
    {
        IMethodSymbol method = methodDirective.Method;
        string returnType = method.ReturnType.ToDisplayString();
        string typeName = command.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string methodName = EscapeIdentifier(method.Name);
        string handlerName = string.Concat("__MauveInvokeDirective_", index.ToString(CultureInfo.InvariantCulture));
        string arguments = method.Parameters.Length == 0
            ? string.Empty
            : IsNamedType(method.Parameters[0].Type, "System.Threading", "CancellationToken")
                ? "cancellationToken"
                : "context.CommandContext";
        string invocation = string.Concat("instance.", methodName, "(", arguments, ")");
        _ = builder.Append("    private static async global::System.Threading.Tasks.ValueTask<global::Mauve.Cli.Invocation.DirectiveResult> ")
            .Append(handlerName).AppendLine("(global::Mauve.Cli.Invocation.DirectiveInvocationContext context, global::System.Threading.CancellationToken cancellationToken)");
        _ = builder.AppendLine("    {");
        _ = builder.AppendLine("        await global::System.Threading.Tasks.ValueTask.CompletedTask;");
        _ = builder.Append("        ").Append(typeName).AppendLine(" instance = new();");
        if (returnType == "void")
        {
            _ = builder.Append("        ").Append(invocation).AppendLine(";");
            _ = builder.AppendLine("        return global::Mauve.Cli.Invocation.DirectiveResult.ContinueProcessing;");
        }
        else if (returnType == "int")
        {
            _ = builder.Append("        int result = ").Append(invocation).AppendLine(";");
            EmitIntDirectiveResult(builder);
        }
        else if (returnType == "Mauve.Cli.Invocation.DirectiveResult")
        {
            _ = builder.Append("        return ").Append(invocation).AppendLine(";");
        }
        else
        {
            string taskType = returnType;
            if (returnType == "System.Threading.Tasks.Task")
            {
                _ = builder.Append("        global::System.Threading.Tasks.Task task = ").Append(invocation).AppendLine(";");
                _ = builder.AppendLine("        if (task is null) throw new global::System.InvalidOperationException(\"The asynchronous directive returned a null task.\");");
                _ = builder.AppendLine("        await task.ConfigureAwait(false);");
                _ = builder.AppendLine("        return global::Mauve.Cli.Invocation.DirectiveResult.ContinueProcessing;");
            }
            else if (returnType == "System.Threading.Tasks.ValueTask")
            {
                _ = builder.Append("        await ").Append(invocation).AppendLine(".ConfigureAwait(false);");
                _ = builder.AppendLine("        return global::Mauve.Cli.Invocation.DirectiveResult.ContinueProcessing;");
            }
            else
            {
                bool resultIsDirective = taskType.EndsWith("<Mauve.Cli.Invocation.DirectiveResult>", StringComparison.Ordinal);
                string resultType = resultIsDirective ? "global::Mauve.Cli.Invocation.DirectiveResult" : "int";
                if (taskType.StartsWith("System.Threading.Tasks.Task<", StringComparison.Ordinal))
                {
                    _ = builder.Append("        global::System.Threading.Tasks.Task<").Append(resultType).Append("> task = ")
                        .Append(invocation).AppendLine(";");
                    _ = builder.AppendLine("        if (task is null) throw new global::System.InvalidOperationException(\"The asynchronous directive returned a null task.\");");
                    _ = builder.Append("        ").Append(resultType).Append(" result = await task.ConfigureAwait(false);").AppendLine();
                }
                else
                {
                    _ = builder.Append("        ").Append(resultType).Append(" result = await ")
                        .Append(invocation).AppendLine(".ConfigureAwait(false);");
                }

                if (resultIsDirective)
                {
                    _ = builder.AppendLine("        return result;");
                }
                else
                {
                    EmitIntDirectiveResult(builder);
                }
            }
        }

        _ = builder.AppendLine("    }");
    }

    private static void EmitIntDirectiveResult(StringBuilder builder)
    {
        _ = builder.AppendLine("        return result == 0");
        _ = builder.AppendLine("            ? global::Mauve.Cli.Invocation.DirectiveResult.ContinueProcessing");
        _ = builder.AppendLine("            : global::Mauve.Cli.Invocation.DirectiveResult.StopProcessing(result);");
    }

    private static string GeneratePackMembers(PackInfo pack)
    {
        StringBuilder builder = new();
        string namespaceName = pack.Symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : pack.Symbol.ContainingNamespace.ToDisplayString();
        string suffix = GetStableSuffix(pack.Symbol);
        string typeName = EscapeIdentifier(pack.Symbol.Name);
        _ = builder.AppendLine("#nullable enable");
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
            OptionInfo option = pack.Options[index];
            _ = builder.Append("    private static readonly global::Mauve.Cli.Definitions.OptionDefinition ")
                .Append(OptionInfo.FieldName(suffix, index)).Append(" = ")
                .Append(GenerateOption(option)).AppendLine(";");
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
            _ = builder.Append("        _ = pack.AddOption(")
                .Append(OptionInfo.FieldName(suffix, index)).AppendLine(");");
        }

        _ = builder.AppendLine("        return pack;");
        _ = builder.AppendLine("    }");
        _ = builder.Append("    internal static ").Append(typeName).Append(" __MauveCreatePack_").Append(suffix)
            .Append('(').Append(_packContextType).AppendLine(" context)");
        _ = builder.AppendLine("    {");
        _ = builder.Append("        ").Append(typeName).Append(" instance = new(");
        EmitPackConstructorArguments(builder, pack.Inclusions, "context", nested: true);
        _ = builder.AppendLine(")");
        EmitObjectInitializers(builder, pack.Options, [], suffix);
        _ = builder.AppendLine("        return instance;");
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

    private static void EmitPackConstructorArguments(
        StringBuilder builder,
        List<PackInclusionInfo> inclusions,
        string context,
        bool nested)
    {
        for (int index = 0; index < inclusions.Count; index++)
        {
            PackInclusionInfo inclusion = inclusions[index];
            if (index > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(inclusion.Pack.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(".__MauveCreatePack_").Append(GetStableSuffix(inclusion.Pack.Symbol))
                .Append('(').Append(context).Append(nested ? ".GetNestedPack(" : ".GetOptionPack(")
                .Append(Literal(inclusion.Key)).Append("))");
        }
    }

    private static string GenerateOption(OptionInfo option)
        => GenerateOption(option.Attribute, option.Property.Type, option.Property.Name, option.Description,
            GeneratePropertyConverter(option.Property), IsRequiredOption(option.Attribute, option.Property), option.Default);

    private static string GenerateOption(
        AttributeData attribute,
        ITypeSymbol valueType,
        string? inferredMemberName = null,
        string? description = null,
        string converter = "",
        bool isRequired = false,
        DefaultInfo? defaultInfo = null)
    {
        string? name = GetOptionLongName(attribute, inferredMemberName);
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
        string shortAliases = string.Join(", ", GetNamedChars(attribute, "ShortAliases").Select(static alias => $"'{alias}'"));
        char declaredShort = GetNamedChar(attribute, "ShortName");
        string shortName = declaredShort != '\0'
            ? $", shortName: '{declaredShort}'"
            : string.Empty;
        string barePolicy = GetNamedInt(attribute, "BareValuePolicy") == 1
            ? ", bareValuePolicy: global::Mauve.Cli.Definitions.BareValuePolicy.Null"
            : string.Empty;
        string environmentBinding = string.Concat(
            ", environmentVariableBinding: global::Mauve.Cli.Definitions.EnvironmentVariableBinding.",
            EnumName("EnvironmentVariableBinding", GetNamedInt(attribute, "EnvironmentVariableBinding")));
        string environmentName = string.Concat(
            ", environmentVariableName: ",
            Literal(GetNamedString(attribute, "EnvironmentVariableName")));
        return string.Concat(
            "new global::Mauve.Cli.Definitions.OptionDefinition(",
            name is null ? "null" : Literal(name),
            ", typeof(",
            typeForTypeof,
            "), global::Mauve.Cli.Definitions.OptionArity.",
            arity,
            elementType,
            ", aliases: new global::Mauve.Cli.Definitions.OptionAlias[] { ",
            aliases,
            " }, allowsNull: ",
            GetNamedBoolean(attribute, "AllowsNull") || IsNullable(GetScalarType(valueType)) ? "true" : "false",
            ", isRequired: ",
            isRequired ? "true" : "false",
            ", description: ",
            Literal(description ?? GetNamedString(attribute, "Description")),
            ", valueLabel: ",
            Literal(GetNamedString(attribute, "ValueLabel")),
            ", isHidden: ",
            GetNamedBoolean(attribute, "IsHidden") ? "true" : "false",
            ", isSensitive: ",
            GetNamedBoolean(attribute, "IsSensitive") ? "true" : "false",
            ", scope: global::Mauve.Cli.Definitions.OptionScope.",
            EnumName("OptionScope", GetNamedInt(attribute, "Scope")),
            ", shortAliases: new char[] { ",
            shortAliases,
            " }",
            barePolicy,
            shortName,
            GenerateDefault(defaultInfo),
            converter,
            environmentBinding,
            environmentName,
            ")");
    }

    private static string GenerateArgument(ArgumentInfo argument)
    {
        string name = GetConstructorString(argument.Attribute, 0)
            ?? DeriveMemberName(argument.Property.Name);
        (int minimum, int maximum) = GetArgumentBounds(argument.Attribute, argument.Property);
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
            GetNamedBoolean(argument.Attribute, "AllowsNull") || IsNullable(GetScalarType(argument.Property.Type)) ? "true" : "false",
            ", description: ",
            Literal(argument.Description),
            ", isHidden: ",
            GetNamedBoolean(argument.Attribute, "IsHidden") ? "true" : "false",
            ", isSensitive: ",
            GetNamedBoolean(argument.Attribute, "IsSensitive") ? "true" : "false",
            GenerateDefault(argument.Default),
            GeneratePropertyConverter(argument.Property),
            ")");
    }

    private static string GenerateDefault(DefaultInfo? defaultInfo) => defaultInfo is null
        ? string.Empty
        : string.Concat(", defaultValuePolicy: global::Mauve.Cli.Definitions.DefaultValuePolicy.Constant, defaultValue: ",
            defaultInfo.Expression, ", defaultDisplay: ", Literal(defaultInfo.Display));

    private static string GeneratePropertyConverter(IPropertySymbol property)
    {
        AttributeData? attribute = FindAttribute(property, _converterAttributeName);
        if (attribute?.AttributeClass is not INamedTypeSymbol providerAttribute)
        {
            return string.Empty;
        }

        ITypeSymbol target = GetScalarType(property.Type);
        return string.Concat(
            ", converter: global::Mauve.Cli.Conversion.ValueConverterRegistration.Create<",
            FormatType(target), ", ", FormatType(providerAttribute.TypeArguments[0]),
            ">(completionSafe: ", GetNamedBoolean(attribute, "CompletionSafe") ? "true" : "false", ")");
    }

    private static ITypeSymbol GetScalarType(ITypeSymbol type)
        => TryGetCollectionElementType(type, out ITypeSymbol? element) && element is not null ? element : type;

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        => type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? named.TypeArguments[0] : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

    private static bool ReadConversionRegistrations(SourceProductionContext context, CommandInfo root, Compilation compilation)
    {
        List<IPropertySymbol> properties = [];
        HashSet<INamedTypeSymbol> packs = [with(SymbolEqualityComparer.Default)];
        CollectConversionProperties(root, properties, packs);
        HashSet<ITypeSymbol> active = [with(SymbolEqualityComparer.Default)];
        HashSet<ITypeSymbol> visited = [with(SymbolEqualityComparer.Default)];
        bool valid = true;
        foreach (IPropertySymbol property in properties)
        {
            ITypeSymbol target = GetScalarType(property.Type);
            AttributeData? local = FindAttribute(property, _converterAttributeName);
            if (property.GetAttributes().Count(static attribute => IsAttribute(attribute, _converterAttributeName)) > 1)
            {
                Report(context, _invalidMember, GetLocation(local, property), "A bound property can declare only one converter override.");
                valid = false;
                continue;
            }
            if (local is not null)
            {
                valid &= ValidateConverterProvider(context, local, target, root.Symbol, compilation);
                AttributeData? option = FindAttribute(property, _optionAttributeName);
                if (option is not null && GetOptionArity(option, property.Type) == 2
                    && FindAttribute(property.ContainingType, _directiveAttributeName) is null)
                {
                    Report(context, _invalidMember, GetLocation(local, property), "Boolean flags use intrinsic literal parsing; use scalar arity for a converter.");
                    valid = false;
                }
            }
            else
            {
                valid &= CollectConversionType(context, target, root, compilation, active, visited, property);
            }
        }

        return valid;
    }

    private static void CollectConversionProperties(CommandInfo command, List<IPropertySymbol> properties, HashSet<INamedTypeSymbol> packs)
    {
        properties.AddRange(command.Options.Select(static option => option.Property));
        properties.AddRange(command.Arguments.Select(static argument => argument.Property));
        foreach (PackInclusionInfo inclusion in command.Packs)
        {
            CollectPackConversionProperties(inclusion.Pack, properties, packs);
        }

        foreach (DirectiveRegistrationInfo directive in command.Directives)
        {
            if (directive.Directive is not null)
            {
                properties.AddRange(directive.Directive.Values.Select(static value => value.Property));
            }
        }

        foreach (ChildInfo child in command.Children)
        {
            CollectConversionProperties(child.Child, properties, packs);
        }
    }

    private static void CollectPackConversionProperties(PackInfo pack, List<IPropertySymbol> properties, HashSet<INamedTypeSymbol> packs)
    {
        if (!packs.Add(pack.Symbol))
        {
            return;
        }

        properties.AddRange(pack.Options.Select(static option => option.Property));
        foreach (PackInclusionInfo inclusion in pack.Inclusions)
        {
            CollectPackConversionProperties(inclusion.Pack, properties, packs);
        }
    }

    private static bool CollectConversionType(
        SourceProductionContext context,
        ITypeSymbol target,
        CommandInfo root,
        Compilation compilation,
        HashSet<ITypeSymbol> active,
        HashSet<ITypeSymbol> visited,
        ISymbol location)
    {
        ITypeSymbol type = UnwrapNullable(target);
        AttributeData? provider = FindAttribute(type, _converterAttributeName);
        if (type.GetAttributes().Count(static attribute => IsAttribute(attribute, _converterAttributeName)) > 1)
        {
            Report(context, _invalidMember, GetLocation(provider, location), "A value type can declare only one converter provider.");
            return false;
        }
        bool isUnion = !type.UnionCaseTypes.IsDefaultOrEmpty;
        if (provider is null && type.TypeKind != TypeKind.Enum && !isUnion)
        {
            return true;
        }

        if (!SymbolEqualityComparer.Default.Equals(target, type)
            && !root.NullableConverters.Contains(type, SymbolEqualityComparer.Default))
        {
            root.NullableConverters.Add(type);
        }

        if (active.Contains(type))
        {
            Report(context, _invalidMember, location.Locations.FirstOrDefault() ?? Location.None, "CLI union conversion cannot contain recursive case dependencies.");
            return false;
        }

        if (!visited.Add(type))
        {
            return true;
        }

        if (provider is not null)
        {
            if (!ValidateConverterProvider(context, provider, type, root.Symbol, compilation))
            {
                return false;
            }
        }
        else if (type.TypeKind == TypeKind.Enum && !type.GetMembers().OfType<IFieldSymbol>().Any(static field => field.HasConstantValue))
        {
            Report(context, _invalidMember, location.Locations.FirstOrDefault() ?? Location.None, "An enum without named members requires an explicit converter.");
            return false;
        }
        else if (isUnion)
        {
            if (!type.DeclaringSyntaxReferences.Any(static reference => reference.GetSyntax() is UnionDeclarationSyntax))
            {
                Report(context, _invalidMember, location.Locations.FirstOrDefault() ?? Location.None, "Automatic CLI conversion supports source union declarations; register custom unions explicitly.");
                return false;
            }

            _ = active.Add(type);
            foreach (ITypeSymbol unionCase in type.UnionCaseTypes)
            {
                if (!compilation.IsSymbolAccessibleWithin(unionCase, root.Symbol)
                    || !CollectConversionType(context, unionCase, root, compilation, active, visited, location))
                {
                    Report(context, _invalidMember, location.Locations.FirstOrDefault() ?? Location.None, "Union cases must be accessible and have nonrecursive conversion metadata.");
                    return false;
                }
            }

            _ = active.Remove(type);
        }

        root.ConversionTypes.Add(type);
        return true;
    }

    private static bool ValidateConverterProvider(
        SourceProductionContext context,
        AttributeData attribute,
        ITypeSymbol target,
        INamedTypeSymbol root,
        Compilation compilation)
    {
        ITypeSymbol? provider = attribute.AttributeClass?.TypeArguments.FirstOrDefault();
        bool valid = provider is INamedTypeSymbol named
            && compilation.IsSymbolAccessibleWithin(named, root)
            && named.AllInterfaces.Any(contract =>
                contract.OriginalDefinition.ToDisplayString() == "Mauve.Cli.Conversion.ICliValueConverter<T>"
                && SymbolEqualityComparer.IncludeNullability.Equals(contract.TypeArguments[0], target));
        if (!valid)
        {
            Report(context, _invalidMember, GetLocation(attribute, root), "CliConverter requires an accessible static ICliValueConverter<T> provider matching the exact scalar or element type.");
        }

        return valid;
    }

    private static void EmitConversionRegistrations(StringBuilder builder, CommandInfo root)
    {
        if (root.ConversionTypes.Count == 0)
        {
            return;
        }

        _ = builder.AppendLine("        global::Mauve.Cli.Conversion.ConverterRegistryBuilder converters = new();");
        foreach (ITypeSymbol type in root.ConversionTypes)
        {
            string typeName = FormatType(type);
            AttributeData? provider = FindAttribute(type, _converterAttributeName);
            if (provider?.AttributeClass is INamedTypeSymbol attributeClass)
            {
                _ = builder.Append("        _ = converters.Register<").Append(typeName).Append(", ")
                    .Append(FormatType(attributeClass.TypeArguments[0])).Append(">(completionSafe: ")
                    .Append(GetNamedBoolean(provider, "CompletionSafe") ? "true" : "false").AppendLine(");");
            }
            else if (type.TypeKind == TypeKind.Enum)
            {
                _ = builder.Append("        _ = converters.RegisterEnum<").Append(typeName).AppendLine(">(new global::System.Collections.Generic.KeyValuePair<string, " + typeName + ">[]");
                _ = builder.AppendLine("        {");
                foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(static field => field.HasConstantValue))
                {
                    _ = builder.Append("            new(").Append(Literal(field.Name)).Append(", ").Append(typeName)
                        .Append('.').Append(EscapeIdentifier(field.Name)).AppendLine("),");
                }

                _ = builder.AppendLine("        });");
            }
            else
            {
                _ = builder.Append("        _ = converters.RegisterUnion<").Append(typeName).AppendLine(">(union => union");
                foreach (ITypeSymbol unionCase in type.UnionCaseTypes)
                {
                    _ = builder.Append("            .Case<").Append(FormatType(unionCase)).Append(">(static value => new ")
                        .Append(typeName).AppendLine("(value))");
                }

                _ = builder.AppendLine("        );");
            }
        }

        foreach (ITypeSymbol type in root.NullableConverters)
        {
            _ = builder.Append("        _ = converters.RegisterNullable<").Append(FormatType(type)).AppendLine(">();");
        }

        _ = builder.AppendLine("        _ = command.AddConverters(converters.Build());");
    }

    private static void EmitObjectInitializers(
        StringBuilder builder,
        List<OptionInfo> options,
        List<ArgumentInfo> arguments,
        string suffix)
    {
        _ = builder.AppendLine("        {");
        for (int index = 0; index < options.Count; index++)
        {
            EmitObjectInitializer(builder, options[index].Property, OptionInfo.FieldName(suffix, index), "Option", index);
        }

        for (int index = 0; index < arguments.Count; index++)
        {
            EmitObjectInitializer(builder, arguments[index].Property, ArgumentInfo.FieldName(suffix, index), "Positional", index);
        }

        _ = builder.AppendLine("        };");
    }

    private static void EmitObjectInitializer(
        StringBuilder builder,
        IPropertySymbol property,
        string definitionField,
        string kind,
        int index)
    {
        string variable = string.Concat("__mauve", kind, index.ToString(CultureInfo.InvariantCulture));
        _ = builder.Append("            ").Append(EscapeIdentifier(property.Name)).Append(" = context.TryGet")
            .Append(kind).Append("Value<").Append(FormatType(property.Type)).Append(">(")
            .Append(definitionField).Append(", out var ").Append(variable)
            .Append(") ? ").Append(variable).AppendLine("! : default!,");
    }

    private static void EmitHandler(
        StringBuilder builder,
        CommandInfo command,
        string suffix)
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
        _ = builder.Append("        ").Append(fullyQualifiedTypeName).Append(" instance = new(");
        EmitPackConstructorArguments(builder, command.Packs, "context", nested: false);
        _ = builder.AppendLine(")");
        EmitObjectInitializers(builder, command.Options, command.Arguments, suffix);
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
        string normalized = name.StartsWith("--", StringComparison.Ordinal) ? name.Substring(2) : name;
        return normalized.Length is >= 1 and <= 126
            && IsAsciiLetter(normalized[0])
            && normalized.Skip(1).All(static character => IsAsciiAlphaNumeric(character) || character == '-');
    }

    private static bool IsValidOptionAlias(string name)
    {
        return (name.Length == 2 && name[0] == '-' && name[1] != '-' && IsAsciiLetter(name[1]))
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
            "OptionArity" => ["RequiredScalar", "OptionalScalar", "BooleanFlag", "RepeatedSingleCollection", "GreedyCollection", "Counting"],
            "OptionScope" => ["Local", "Inherited"],
            "EnvironmentVariableBinding" => ["Inherit", "Enabled", "Disabled"],
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
            Accessibility.Private => "private",
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

    private static string GetPackKey(INamedTypeSymbol symbol)
    {
        string identity = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        using SHA256 algorithm = SHA256.Create();
        byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(identity));
        return string.Concat("p", BitConverter.ToString(hash).Replace("-", string.Empty));
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

    private static string? GetDisplayString(AttributeData attribute, string name, string? fallback)
        => HasNamedArgument(attribute, name) ? GetNamedString(attribute, name) : fallback;

    private static CliXmlDocumentation ReadDocumentation(
        SourceProductionContext context,
        ISymbol symbol,
        AttributeData attribute,
        bool readExamples)
    {
        CliXmlDocumentation documentation = CliXmlDocumentation.Read(symbol, readExamples, context.CancellationToken);
        if (!documentation.IsValid)
        {
            Report(context, _invalidMember, GetLocation(attribute, symbol),
                "CLI documentation must be valid XML and shell examples must contain a non-empty, single-line code element.");
        }

        return documentation;
    }

    private static int GetNamedInt(AttributeData attribute, string name, int defaultValue = 0)
    {
        KeyValuePair<string, TypedConstant> value = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name);
        return value.Key is null
            ? defaultValue
            : Convert.ToInt32(value.Value.Value, CultureInfo.InvariantCulture);
    }

    private static char GetNamedChar(AttributeData attribute, string name)
    {
        TypedConstant constant = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value;
        return constant.Value is char value ? value : '\0';
    }

    private static char[] GetNamedChars(AttributeData attribute, string name)
    {
        TypedConstant constant = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == name).Value;
        return constant.IsNull || constant.Values.IsDefault
            ? []
            : [.. constant.Values.Where(static value => value.Value is char).Select(static value => (char)value.Value!)];
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

    private static bool IsRequiredOption(AttributeData attribute, IPropertySymbol property)
        => HasNamedArgument(attribute, "IsRequired")
            ? GetNamedBoolean(attribute, "IsRequired")
            : property.IsRequired || FindAttribute(property, "System.ComponentModel.DataAnnotations.RequiredAttribute") is not null;

    private static (int Minimum, int Maximum) GetArgumentBounds(
        AttributeData attribute,
        IPropertySymbol property)
    {
        bool collection = TryGetCollectionElementType(property.Type, out _);
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

    private static string DeriveDirectiveName(string typeName)
    {
        const string suffix = "Directive";
        string name = typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - suffix.Length)
            : typeName;
        return DeriveMemberName(name);
    }

    private static string DeriveOptionName(string memberName) => DeriveMemberName(memberName);

    private static string? GetOptionLongName(AttributeData attribute, string? inferredMemberName)
    {
        string? explicitName = GetConstructorString(attribute, 0);
        return explicitName is not null
            ? explicitName.StartsWith("--", StringComparison.Ordinal) ? explicitName.Substring(2) : explicitName
            : GetNamedChar(attribute, "ShortName") != '\0' || inferredMemberName is null
                ? null
                : DeriveOptionName(inferredMemberName);
    }

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

        public List<string> Examples { get; } = [];

        public List<PackInclusionInfo> Packs { get; } = [];

        public List<DirectiveRegistrationInfo> Directives { get; } = [];

        public List<ITypeSymbol> ConversionTypes { get; } = [];

        public List<ITypeSymbol> NullableConverters { get; } = [];

        public IMethodSymbol? Handler { get; set; }

        public bool IsValid { get; set; } = true;
    }

    private sealed class DirectiveInfo(
        INamedTypeSymbol symbol,
        AttributeData attribute,
        string name)
    {
        public INamedTypeSymbol Symbol { get; } = symbol;

        public AttributeData Attribute { get; } = attribute;

        public string Name { get; } = name;

        public string? Description => GetNamedString(Attribute, "Description");

        public bool IsHidden => GetNamedBoolean(Attribute, "IsHidden");

        public List<OptionInfo> Values { get; } = [];

        public IMethodSymbol? Handler { get; set; }

        public bool IsValid { get; set; } = true;

        public static string FieldName(string suffix, int index) => string.Concat("__mauveDirectiveValue_", suffix, "_", index.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class MethodDirectiveInfo(
        IMethodSymbol method,
        AttributeData attribute,
        string name)
    {
        public IMethodSymbol Method { get; } = method;

        public AttributeData Attribute { get; } = attribute;

        public string Name { get; } = name;
    }

    private sealed class DirectiveRegistrationInfo(
        DirectiveInfo? directive,
        MethodDirectiveInfo? methodDirective,
        SourceOrder sourceOrder)
    {
        public DirectiveInfo? Directive { get; } = directive;

        public MethodDirectiveInfo? MethodDirective { get; } = methodDirective;

        public SourceOrder SourceOrder { get; } = sourceOrder;
    }

    private sealed class PackInfo(INamedTypeSymbol symbol, AttributeData attribute, string? title, string? description)
    {
        public INamedTypeSymbol Symbol { get; } = symbol;

        public AttributeData Attribute { get; } = attribute;

        public string Name => Symbol.Name;

        public string? Title { get; } = title;

        public string? Description { get; } = description;

        public List<OptionInfo> Options { get; } = [];

        public List<PackInclusionInfo> Inclusions { get; } = [];

        public bool IsValid { get; set; } = true;
    }

    private sealed class PackInclusionInfo(
        PackInfo pack,
        IParameterSymbol parameter)
    {
        public PackInfo Pack { get; } = pack;

        public string Key => GetPackKey(Pack.Symbol);

        public string? Title => Pack.Title;

        public string? Description => Pack.Description;

        public IParameterSymbol Parameter { get; } = parameter;
    }

    private sealed class OptionInfo(
        IPropertySymbol property,
        AttributeData attribute,
        SourceOrder sourceOrder,
        string? description = null,
        DefaultInfo? defaultInfo = null)
    {
        public IPropertySymbol Property { get; } = property;

        public AttributeData Attribute { get; } = attribute;

        public SourceOrder SourceOrder { get; } = sourceOrder;

        public string? Description { get; } = description ?? GetNamedString(attribute, "Description");

        public DefaultInfo? Default { get; } = defaultInfo;

        public static string FieldName(string suffix, int index) => string.Concat("__mauveOption_", suffix, "_", index.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class ArgumentInfo(
        IPropertySymbol property,
        AttributeData attribute,
        SourceOrder sourceOrder,
        string? description,
        DefaultInfo? defaultInfo)
    {
        public IPropertySymbol Property { get; } = property;

        public AttributeData Attribute { get; } = attribute;

        public SourceOrder SourceOrder { get; } = sourceOrder;

        public string? Description { get; } = description;
        public DefaultInfo? Default { get; } = defaultInfo;
        public static string FieldName(string suffix, int index) => string.Concat("__mauveArgument_", suffix, "_", index.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class DefaultInfo(object? value, string expression, string display)
    {
        public object? Value { get; } = value;
        public string Expression { get; } = expression;
        public string Display { get; } = display;
    }

    private sealed class ChildInfo(
        CommandInfo parent,
        CommandInfo child,
        AttributeData attribute,
        SourceOrder sourceOrder,
        bool isParentDeclaration)
    {
        public CommandInfo Parent { get; } = parent;

        public CommandInfo Child { get; } = child;

        public AttributeData Attribute { get; } = attribute;

        public SourceOrder SourceOrder { get; } = sourceOrder;

        public bool IsParentDeclaration { get; } = isParentDeclaration;

        public CommandInfo DeclaringCommand => IsParentDeclaration ? Child : Parent;

        public bool IsValid { get; set; } = true;
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
