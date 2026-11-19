using System;
using System.Linq;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Invocation;

/// <summary>Provides invocation-local values for one included option pack.</summary>
public sealed class OptionPackContext
{
    private readonly ParseResult _parseResult;
    private readonly CommandDefinition _owner;
    private readonly OptionPackSnapshot _pack;

    internal OptionPackContext(
        ParseResult parseResult,
        CommandDefinition owner,
        OptionPackSnapshot pack)
    {
        _parseResult = parseResult;
        _owner = owner;
        _pack = pack;
    }

    /// <summary>Gets the inclusion key within the owning command or pack.</summary>
    public string Key => _pack.Key;

    /// <summary>Gets the optional help-group title on this inclusion.</summary>
    public string? Title => _pack.Title;

    /// <summary>Gets the help-group description on this inclusion.</summary>
    public string Description => _pack.Description;

    /// <summary>Gets a directly nested pack by its inclusion key.</summary>
    /// <param name="key">The nested inclusion key.</param>
    /// <returns>The nested invocation-local view.</returns>
    public OptionPackContext GetNestedPack(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        OptionPackSnapshot? nested = _pack.NestedPacks.FirstOrDefault(pack =>
            string.Equals(pack.Key, key, StringComparison.Ordinal));
        return nested is null
            ? throw new ArgumentException("The nested pack key does not belong to this pack.", nameof(key))
            : new OptionPackContext(_parseResult, _owner, nested);
    }

    /// <summary>Attempts to get a typed value declared by this pack or a nested pack.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="option">The option definition handle.</param>
    /// <param name="value">The value when available.</param>
    /// <returns>True when the value is available.</returns>
    public bool TryGetOptionValue<T>(OptionDefinition option, out T? value)
    {
        ArgumentNullException.ThrowIfNull(option);
        return !_pack.ContainsOption(option)
            ? throw new ArgumentException("The option handle is not part of this pack inclusion.", nameof(option))
            : _parseResult.TryGetOptionValue(_owner, option, out value);
    }
}
