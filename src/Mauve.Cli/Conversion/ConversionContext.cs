using System;
using System.Globalization;

namespace Mauve.Cli.Conversion;

/// <summary>Provides bounded metadata to an explicitly registered converter.</summary>
public sealed class ConversionContext
{
    internal ConversionContext(
        Type targetType,
        string definitionName,
        int tokenIndex,
        CultureInfo numericCulture,
        bool isSensitive)
    {
        TargetType = targetType;
        DefinitionName = definitionName;
        TokenIndex = tokenIndex;
        NumericCulture = numericCulture;
        IsSensitive = isSensitive;
    }

    /// <summary>Gets the declared target type.</summary>
    public Type TargetType { get; }

    /// <summary>Gets the safe definition name.</summary>
    public string DefinitionName { get; }

    /// <summary>Gets the zero-based token index.</summary>
    public int TokenIndex { get; }

    /// <summary>Gets the explicit numeric culture for this parse.</summary>
    public CultureInfo NumericCulture { get; }

    /// <summary>Gets whether the value must be treated as sensitive.</summary>
    public bool IsSensitive { get; }
}
