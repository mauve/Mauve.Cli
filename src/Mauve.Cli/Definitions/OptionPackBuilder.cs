using System;
using System.Collections.Generic;

namespace Mauve.Cli.Definitions;

/// <summary>Builds a reusable group of options and nested pack inclusions.</summary>
public sealed class OptionPackBuilder
{
    private readonly List<OptionDefinition> _options = [];
    private readonly List<OptionPackInclusionBuilder> _packs = [];

    /// <summary>Adds an option owned by this pack.</summary>
    /// <param name="option">The immutable option definition.</param>
    /// <returns>This builder.</returns>
    public OptionPackBuilder AddOption(OptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);
        _options.Add(option);
        return this;
    }

    /// <summary>Adds a nested pack with an explicit identity key.</summary>
    /// <param name="key">The inclusion key, unique among this pack's nested inclusions.</param>
    /// <param name="pack">The nested pack builder.</param>
    /// <param name="title">An optional help-group title.</param>
    /// <param name="description">An optional help-group description.</param>
    /// <returns>This builder.</returns>
    public OptionPackBuilder AddPack(
        string key,
        OptionPackBuilder pack,
        string? title = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(pack);
        _packs.Add(new OptionPackInclusionBuilder(key, pack, title, description));
        return this;
    }

    internal OptionDefinition[] CopyOptions() => [.. _options];

    internal OptionPackInclusionBuilder[] CopyPacks() => [.. _packs];

    internal int OptionCount => _options.Count;

    internal int PackCount => _packs.Count;
}

internal sealed record OptionPackInclusionBuilder(
    string Key,
    OptionPackBuilder Pack,
    string? Title,
    string? Description);

internal sealed class OptionPackSnapshot(
    string key,
    OptionDefinition[] options,
    OptionPackSnapshot[] nestedPacks,
    string? title,
    string? description)
{
    public string Key { get; } = key;

    public OptionDefinition[] Options { get; } = options;

    public OptionPackSnapshot[] NestedPacks { get; } = nestedPacks;

    public string? Title { get; } = title;

    public string? Description { get; } = description;

    public bool ContainsOption(OptionDefinition option)
    {
        foreach (OptionDefinition candidate in Options)
        {
            if (ReferenceEquals(candidate, option))
            {
                return true;
            }
        }

        foreach (OptionPackSnapshot nested in NestedPacks)
        {
            if (nested.ContainsOption(option))
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly record struct OptionPackGroupMetadata(
    string InclusionPathKey,
    string? GroupKey,
    string? Title,
    string? Description);
