using System;

namespace Mauve.Cli.Declarations;

/// <summary>Chooses whether a validation rule checks the complete property value or each item in a collection.</summary>
/// <remarks>
/// <para>
/// All property validation attributes default to <see cref="Value"/>.
/// For a scalar property, keep that default. On an <c>IReadOnlyList&lt;T&gt;</c>,
/// choose <see cref="Items"/> to apply a rule to each <c>T</c> rather than to the list.
/// Choosing Items on a scalar is a generator error.
/// </para>
/// <para>
/// Validation runs after successful conversion and default handling, before the command
/// handler. It also checks available default and environment values. An unavailable
/// optional value does not run property validators. These rules do not change how many
/// tokens an option consumes or make an absent option required.
/// </para>
/// <para>
/// Each item rule checks items in order and reports its first rejection, with the
/// one-based item number in the public reason and the original supplying token when known.
/// Different rules can report additional failures. Default and environment values do not
/// receive invented command-line positions.
/// </para>
/// </remarks>
/// <example>
/// Check the number of tags separately from the length of each tag:
/// <code language="csharp"><![CDATA[
/// [CliOption("tag", Arity = OptionArity.RepeatedSingleCollection)]
/// [CliLength(1, 4)]
/// [CliLength(2, 12, Target = CliValidationTarget.Items)]
/// public required IReadOnlyList<string> Tags { get; init; }
/// ]]></code>
/// This accepts one to four tags, each with a string length between 2 and 12.
/// The snippet uses <c>Mauve.Cli.Declarations</c>, <c>Mauve.Cli.Definitions</c>
/// and <c>System.Collections.Generic</c>.
/// </example>
public enum CliValidationTarget
{
    /// <summary>Check the complete value. For a length rule on a collection, check its item count.</summary>
    /// <remarks>
    /// This is the default. For example, <c>[CliMaxLength(4)]</c> limits a string
    /// to length 4 or a collection to four items; it does not limit each string in a list.
    /// </remarks>
    Value,

    /// <summary>Check each collection item separately, keeping its original input location for failures.</summary>
    /// <remarks>
    /// For example, <c>[CliMinValue(1, Target = CliValidationTarget.Items)]</c>
    /// requires each numeric item to be at least 1. It does not impose a minimum item count.
    /// An empty collection has no items to check; add a whole-value length or nonempty rule
    /// when the collection itself must contain items.
    /// </remarks>
    Items
}

/// <summary>Requires a numeric, date, time or duration value to be between two bounds, including both endpoints.</summary>
/// <remarks>
/// <para>
/// Use on a command option, positional argument or option-pack option. Standard integer
/// types, <c>float</c>, <c>double</c>, <c>decimal</c>, <c>DateOnly</c>,
/// <c>TimeOnly</c>, <c>DateTimeOffset</c> and <c>TimeSpan</c> are supported,
/// including nullable forms. This is not a general comparer for strings or arbitrary objects.
/// </para>
/// <para>
/// Use numeric constructor arguments for whole-number bounds that fit in <c>long</c>.
/// Use strings for fractions, larger unsigned bounds or temporal values. Numeric text uses
/// invariant formatting, with a decimal point rather than a culture-specific decimal comma.
/// Integer targets cannot use fractional bounds. Bounds must fit the supported target and
/// the numeric-text parser's decimal range. Invalid or reversed bounds are generator errors.
/// </para>
/// <para>
/// Temporal bounds use these formats: dates such as <c>2026-01-01</c>; times such as
/// <c>08:30</c> or <c>08:30:15.5</c>; timestamps such as <c>2026-01-01T08:30Z</c>
/// or <c>2026-01-01T08:30:00+02:00</c>; and constant-format durations such as
/// <c>00:05:00</c> or <c>1.02:03:04</c>. Timestamps compare as instants, taking their offsets into account.
/// </para>
/// <para>
/// An available null passes this rule; an absent optional value is not checked.
/// Use requiredness to require input, or a custom validator if an available null is invalid.
/// Use <c>[CliMinValue]</c> or <c>[CliMaxValue]</c> for a one-sided bound,
/// and length attributes for string length or collection count.
/// </para>
/// </remarks>
/// <example>
/// These property declarations illustrate integer, fractional and date bounds:
/// <code language="csharp"><![CDATA[
/// [CliOption]
/// [CliRange(1, 50)]
/// public int Limit { get; init; } = 10;
///
/// [CliOption]
/// [CliRange("0.25", "0.75")]
/// public decimal Ratio { get; init; } = 0.5m;
///
/// [CliOption]
/// [CliRange("2026-01-01", "2026-12-31")]
/// public DateOnly? Start { get; init; }
/// ]]></code>
/// <c>--limit 50</c> passes; <c>--limit 51</c> fails with an explanation of the range.
/// Omitting <c>--start</c> is allowed. Add <c>Target = CliValidationTarget.Items</c>
/// to apply a range to the numbers or dates in a collection instead.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliRangeAttribute : Attribute
{
    /// <summary>Creates a range from whole-number bounds, such as <c>[CliRange(1, 50)]</c>.</summary>
    /// <param name="minimumValue">The smallest accepted value. A value equal to this bound passes.</param>
    /// <param name="maximumValue">The largest accepted value. It must not be smaller than the minimum.</param>
    /// <remarks>
    /// The property can still be floating-point or decimal; these constructor arguments
    /// simply express whole-number limits. Choose the string overload for fractional or temporal bounds.
    /// </remarks>
    public CliRangeAttribute(long minimumValue, long maximumValue)
    {
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        Minimum = minimumValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Maximum = maximumValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Creates a range from invariant numeric or temporal text, such as <c>[CliRange("0.1", "0.9")]</c>.</summary>
    /// <param name="minimum">The smallest accepted value, in the target type's supported invariant bound format.</param>
    /// <param name="maximum">The largest accepted value, in the same format. It must not be smaller than the minimum.</param>
    /// <remarks>
    /// Text is checked during generation, not interpreted using the user's current culture.
    /// Quotes describe the attribute bounds; they do not change the property's type or its converter.
    /// See the attribute's remarks for date, time, timestamp and duration examples.
    /// </remarks>
    public CliRangeAttribute(string minimum, string maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>Gets the minimum bound as text, regardless of which constructor was used.</summary>
    /// <remarks>The numeric constructor formats its argument using invariant culture. This is declaration metadata, not the rejected input.</remarks>
    public string Minimum { get; }

    /// <summary>Gets the maximum bound as text, regardless of which constructor was used.</summary>
    /// <remarks>The numeric constructor formats its argument using invariant culture. Values equal to this bound are accepted.</remarks>
    public string Maximum { get; }

    /// <summary>Gets the whole-number minimum supplied to the numeric constructor.</summary>
    /// <remarks>This remains zero when the string constructor is used, even if its text describes another value. Use <see cref="Minimum"/> for constructor-independent metadata.</remarks>
    public long MinimumValue { get; }

    /// <summary>Gets the whole-number maximum supplied to the numeric constructor.</summary>
    /// <remarks>This remains zero when the string constructor is used. Use <see cref="Maximum"/> when inspecting text-based or temporal bounds.</remarks>
    public long MaximumValue { get; }

    /// <summary>Gets or sets whether to check the property value or each collection item. The default is <c>Value</c>.</summary>
    /// <remarks>
    /// Use <c>Target = CliValidationTarget.Items</c> on a numeric or temporal collection.
    /// A range cannot compare an entire list; use a length attribute to restrict its count.
    /// </remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Requires a numeric, date, time or duration value to be at least a specified bound.</summary>
/// <remarks>
/// <para>
/// The bound itself is accepted. Supported types, invariant text formats and null
/// handling are the same as for <see cref="CliRangeAttribute"/>. This rule leaves the
/// upper bound unrestricted. Use <c>[CliRange]</c> for a two-sided range, or combine
/// this attribute with <c>[CliMaxValue]</c> when separate bounds are clearer.
/// Contradictory minimum and maximum attributes are generator errors.
/// </para>
/// <para>
/// This compares values, not string lengths or collection counts. Use
/// <c>[CliMinLength]</c> for those. Available null values pass, and this rule does not
/// make an absent option required.
/// </para>
/// </remarks>
/// <example>
/// Accept zero or more retries and dates on or after the given day:
/// <code language="csharp"><![CDATA[
/// [CliOption]
/// [CliMinValue(0)]
/// public int Retries { get; init; } = 3;
///
/// [CliOption]
/// [CliMinValue("2026-01-01")]
/// public DateOnly? Start { get; init; }
/// ]]></code>
/// For a fractional minimum, write <c>[CliMinValue("0.5")]</c> on a decimal or floating-point property.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliMinValueAttribute : Attribute
{
    /// <summary>Creates a whole-number lower bound, such as <c>[CliMinValue(0)]</c>.</summary>
    /// <param name="value">The smallest accepted value. Equality is allowed.</param>
    public CliMinValueAttribute(long value)
    {
        Value = value;
    }

    /// <summary>Creates a lower bound from invariant numeric or temporal text.</summary>
    /// <param name="text">The smallest accepted value, such as <c>"0.5"</c> or <c>"2026-01-01"</c>.</param>
    public CliMinValueAttribute(string text)
    {
        Text = text;
    }

    /// <summary>Gets the whole-number bound supplied to the numeric constructor.</summary>
    /// <remarks>It remains zero for the string constructor. Check <see cref="Text"/> before interpreting this as the configured bound.</remarks>
    public long Value { get; }

    /// <summary>Gets the bound supplied to the string constructor, or null when the numeric constructor was used.</summary>
    /// <remarks>This is invariant declaration text, not formatted user input.</remarks>
    public string? Text { get; }

    /// <summary>Gets or sets whether to check the property value or each collection item. The default is <c>Value</c>.</summary>
    /// <remarks>For example, <c>[CliMinValue(1, Target = CliValidationTarget.Items)]</c> requires every numeric item to be at least 1, but does not require the collection to have any items.</remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Requires a numeric, date, time or duration value to be no greater than a specified bound.</summary>
/// <remarks>
/// <para>
/// The bound itself is accepted. Supported types, invariant text formats and null
/// handling are the same as for <see cref="CliRangeAttribute"/>. There is no lower
/// bound unless you add one. Use <c>[CliRange]</c> when both limits belong together,
/// or combine this with <c>[CliMinValue]</c> to state them separately.
/// </para>
/// <para>
/// Use <c>[CliMaxLength]</c>, not this attribute, to limit string length or collection
/// count. Available null values pass. Requiredness and token consumption are unchanged.
/// </para>
/// </remarks>
/// <example>
/// Accept a timeout of at most five minutes:
/// <code language="csharp"><![CDATA[
/// [CliOption]
/// [CliMaxValue("00:05:00")]
/// public TimeSpan? Timeout { get; init; }
/// ]]></code>
/// This alone also allows negative durations. Add <c>[CliMinValue("00:00:00")]</c>
/// to exclude them, or use <c>[CliRange("00:00:00", "00:05:00")]</c>.
/// For a plain number, use a bound such as <c>[CliMaxValue(100)]</c>.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliMaxValueAttribute : Attribute
{
    /// <summary>Creates a whole-number upper bound, such as <c>[CliMaxValue(100)]</c>.</summary>
    /// <param name="value">The largest accepted value. Equality is allowed.</param>
    public CliMaxValueAttribute(long value)
    {
        Value = value;
    }

    /// <summary>Creates an upper bound from invariant numeric or temporal text.</summary>
    /// <param name="text">The largest accepted value, such as <c>"0.9"</c> or <c>"00:05:00"</c>.</param>
    public CliMaxValueAttribute(string text)
    {
        Text = text;
    }

    /// <summary>Gets the whole-number bound supplied to the numeric constructor.</summary>
    /// <remarks>It remains zero for the string constructor. Check <see cref="Text"/> before interpreting this as the configured bound.</remarks>
    public long Value { get; }

    /// <summary>Gets the bound supplied to the string constructor, or null when the numeric constructor was used.</summary>
    /// <remarks>Use the supported invariant numeric or temporal format, not a localized display value.</remarks>
    public string? Text { get; }

    /// <summary>Gets or sets whether to check the property value or each collection item. The default is <c>Value</c>.</summary>
    /// <remarks>Use <c>Target = CliValidationTarget.Items</c> to cap each numeric or temporal item. Use a whole-value length rule to cap the number of items.</remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Requires a string length or collection item count to fall between two limits, including both limits.</summary>
/// <param name="minimum">The smallest allowed length or count. Must be zero or greater.</param>
/// <param name="maximum">The largest allowed length or count. Must be at least the minimum.</param>
/// <remarks>
/// <para>
/// A string is measured using its .NET <c>Length</c>: UTF-16 code units, not bytes or
/// user-perceived characters. Some characters, such as many emoji, count as two units.
/// A collection is measured by its number of items. An available null passes this rule.
/// </para>
/// <para>
/// A minimum string length does not reject whitespace: a string of spaces still has a length.
/// Add <c>[CliNotEmpty]</c> to reject whitespace-only strings. Use
/// <c>[CliMinLength]</c> or <c>[CliMaxLength]</c> for a one-sided limit.
/// This does not change parsing cardinality or require an absent option to appear.
/// Bounds are checked during generation; negative or contradictory bounds are errors.
/// </para>
/// </remarks>
/// <example>
/// Limit both collection size and individual string lengths:
/// <code language="csharp"><![CDATA[
/// [CliOption("tag", Arity = OptionArity.RepeatedSingleCollection)]
/// [CliLength(1, 4)]
/// [CliLength(2, 12, Target = CliValidationTarget.Items)]
/// public required IReadOnlyList<string> Tags { get; init; }
/// ]]></code>
/// <c>--tag red --tag blue</c> passes; a fifth tag fails the count rule.
/// <c>--tag x</c> fails the item-length rule at the token supplying that item.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliLengthAttribute(int minimum, int maximum) : Attribute
{
    /// <summary>Gets the smallest accepted string length or collection count.</summary>
    /// <remarks>Zero allows an empty string or collection; it does not make an argument or option optional.</remarks>
    public int Minimum { get; } = minimum;

    /// <summary>Gets the largest accepted string length or collection count.</summary>
    /// <remarks>A value exactly this long passes. The unit depends on the selected target: string length or number of items.</remarks>
    public int Maximum { get; } = maximum;

    /// <summary>Gets or sets whether to measure the complete value or each collection item. The default is <c>Value</c>.</summary>
    /// <remarks>On a list of strings, Value limits the number of strings; Items limits each string's length. Choosing Items does not impose a count limit.</remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Requires at least a specified string length or number of collection items.</summary>
/// <param name="minimum">The smallest accepted length or count. Must be zero or greater.</param>
/// <remarks>
/// <para>
/// A value exactly at the minimum passes; there is no upper limit from this rule.
/// Strings use UTF-16 code units, as with .NET <c>string.Length</c>. Collections use
/// their item count. Available null values pass, and absent optional values are not checked.
/// </para>
/// <para>
/// Use <c>[CliNotEmpty]</c> instead when your intent is to reject null, empty or
/// whitespace-only strings. A positive minimum length still accepts sufficiently many
/// spaces. Use <c>[CliLength]</c> for paired limits or add <c>[CliMaxLength]</c>.
/// </para>
/// </remarks>
/// <example>
/// Require a name with at least two UTF-16 code units that is not just whitespace:
/// <code language="csharp"><![CDATA[
/// [CliOption]
/// [CliMinLength(2)]
/// [CliNotEmpty]
/// public string Name { get; init; } = "guest";
/// ]]></code>
/// On a collection property, <c>[CliMinLength(2)]</c> instead requires at least two items.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliMinLengthAttribute(int minimum) : Attribute
{
    /// <summary>Gets the smallest accepted length or item count.</summary>
    /// <remarks>Zero adds no lower restriction. Negative values are rejected during generation.</remarks>
    public int Minimum { get; } = minimum;

    /// <summary>Gets or sets whether to measure the complete value or each collection item. The default is <c>Value</c>.</summary>
    /// <remarks>On a string list, <c>Target = CliValidationTarget.Items</c> checks each string's length instead of requiring a minimum list count.</remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Limits the length of a string or the number of items in a collection.</summary>
/// <param name="maximum">The largest accepted length or count. Must be zero or greater.</param>
/// <remarks>
/// <para>
/// A value exactly at the maximum passes. Strings use UTF-16 code units, not bytes;
/// collections use item count. Empty values pass, as do available null values.
/// Add a minimum-length or nonempty rule when empty values must be rejected.
/// </para>
/// <para>
/// Use <c>[CliMaxValue]</c> to limit a numeric value rather than its size.
/// This rule validates the assembled value; it does not stop a collection option from
/// consuming tokens once the count is reached.
/// </para>
/// </remarks>
/// <example>
/// Keep an optional comment short:
/// <code language="csharp"><![CDATA[
/// [CliOption]
/// [CliMaxLength(200)]
/// public string? Comment { get; init; }
/// ]]></code>
/// An omitted comment is allowed. <c>[CliLength(1, 200)]</c> would additionally
/// reject an available empty string, but would still not require the option to appear.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliMaxLengthAttribute(int maximum) : Attribute
{
    /// <summary>Gets the largest accepted length or item count.</summary>
    /// <remarks>Zero allows only empty nonnull values. Negative values are generator errors.</remarks>
    public int Maximum { get; } = maximum;

    /// <summary>Gets or sets whether to measure the complete value or each collection item. The default is <c>Value</c>.</summary>
    /// <remarks>On a string list, Value caps the item count; Items caps the length of each string. Combine separate attributes to apply both restrictions.</remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Restricts a string to a fixed, case-sensitive set of accepted values.</summary>
/// <param name="values">The distinct strings users may supply, such as <c>"safe"</c> and <c>"fast"</c>.</param>
/// <remarks>
/// <para>
/// Comparison is ordinal: capitalization and whitespace must match exactly.
/// For example, allowing <c>"fast"</c> does not allow <c>"FAST"</c> or
/// <c>" fast "</c>. Null is rejected when it is an available value, but an absent
/// optional property is not validated. An empty string is allowed only if explicitly listed.
/// </para>
/// <para>
/// Declare one allowed set per property. It must contain 1 to 512 distinct nonnull
/// strings, each at most 512 UTF-16 code units. Invalid sets are generator errors.
/// The generated runtime model takes an immutable copy of the allowed values.
/// </para>
/// <para>
/// Visible, nonsensitive options and arguments also use this set for value completion
/// and help text. Do not put secrets in attribute literals. Hidden or sensitive definitions
/// do not suggest these values; sensitive definitions suppress allowed-value help and
/// public validation reasons.
/// </para>
/// <para>
/// Use an enum property instead when your application should receive a distinct typed
/// choice rather than a string. Use a custom converter or validator if you need different
/// case handling or more complex matching.
/// </para>
/// </remarks>
/// <example>
/// Offer two modes in help and completion:
/// <code language="csharp"><![CDATA[
/// [CliOption]
/// [CliAllowedValues("safe", "fast")]
/// public string Mode { get; init; } = "safe";
/// ]]></code>
/// <c>--mode fast</c> passes; <c>--mode other</c> reports the accepted set.
/// For a list of mode strings, add <c>Target = CliValidationTarget.Items</c>.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliAllowedValuesAttribute(params string[] values) : Attribute
{
    /// <summary>Gets the strings supplied to this attribute's constructor.</summary>
    /// <remarks>
    /// Their order is used when presenting the set. This is the attribute's array;
    /// changing an attribute instance does not reconfigure the source-generated model.
    /// </remarks>
    public string[] Values { get; } = values;

    /// <summary>Gets or sets whether to check a single string or each string in a collection. The default is <c>Value</c>.</summary>
    /// <remarks>
    /// On <c>IReadOnlyList&lt;string&gt;</c>, select Items. A set cannot validate the
    /// list object itself. Items checks membership, not collection size or uniqueness.
    /// </remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Rejects null, empty or whitespace-only strings, or null and empty collections.</summary>
/// <remarks>
/// <para>
/// On a string, this checks for any non-whitespace content without trimming or changing
/// the value. On a collection with the default target, it checks that at least one item
/// exists; it does not examine whether those items are empty.
/// </para>
/// <para>
/// Choose Items to check each string in a list. Add both whole-value and item rules if
/// the list must contain items and every item must have content. A numeric zero or
/// <c>false</c> is not an empty string or collection; use a range or custom rule for those types.
/// </para>
/// <para>
/// This is value validation, not requiredness. An absent optional scalar does not run
/// its validators. Use <c>IsRequired = true</c> or required-member inference for options,
/// or <c>MinimumValues = 1</c> for arguments, to require input.
/// </para>
/// </remarks>
/// <example>
/// Require a non-whitespace title:
/// <code language="csharp"><![CDATA[
/// [CliOption]
/// [CliNotEmpty]
/// public required string Title { get; init; }
/// ]]></code>
/// An empty title or a title containing only spaces fails. To apply the same check
/// to every item, use <c>[CliNotEmpty(Target = CliValidationTarget.Items)]</c>
/// on an <c>IReadOnlyList&lt;string&gt;</c> property.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliNotEmptyAttribute : Attribute
{
    /// <summary>Gets or sets whether to check the complete value or each collection item. The default is <c>Value</c>.</summary>
    /// <remarks>
    /// On a string list, Value requires a nonempty list; Items rejects empty or
    /// whitespace-only strings in that list. An empty list passes the item rule because there are no items to check.
    /// </remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Uses your own rule to validate a converted property value or each item in a collection.</summary>
/// <typeparam name="TProvider">
/// An accessible type implementing <c>Mauve.Cli.Validation.ICliValidator&lt;T&gt;</c>
/// for the exact target type, including nullability. It supplies a static <c>Validate</c>
/// method; the library does not construct an instance.
/// </typeparam>
/// <remarks>
/// <para>
/// Put this attribute on command options, positional arguments or option-pack options.
/// The default target passes the complete property value. For
/// <c>IReadOnlyList&lt;string&gt;</c>, a whole-value provider implements
/// <c>ICliValidator&lt;IReadOnlyList&lt;string&gt;&gt;</c>; an item provider implements
/// <c>ICliValidator&lt;string&gt;</c> and requires <c>Target = CliValidationTarget.Items</c>.
/// Validation attributes are not supported on standalone directive values.
/// </para>
/// <para>
/// Return <c>ValueValidationOutcome.Success</c> when valid. For invalid input, return
/// a <c>Failure*</c> outcome or throw <c>CliValidationException</c> carrying one.
/// Give the failure a stable code of at most 64 ASCII letters, digits, dots, underscores
/// or hyphens, and an optional public <c>message</c> explaining how to fix the input.
/// The message is sanitized to one line, bounded to 512 UTF-16 code units and suppressed
/// for sensitive or hidden targets. Never include secrets or raw input. Ordinary exception
/// messages are not displayed, and unexpected exceptions propagate as application faults.
/// </para>
/// <para>
/// <c>FailureAt</c> supplies a zero-based UTF-16 offset within the current scalar value.
/// A whole-collection validator can use <c>FailureForItem</c> with a zero-based index
/// to identify a rejected item. An item provider should return an untargeted failure or
/// <c>FailureAt</c>; the wrapper supplies its item index and original token.
/// Invalid targets, indices or offsets are contract faults, not successful validation.
/// </para>
/// <para>
/// The read-only context gives access to assembled values. Do not mutate those values
/// or perform command side effects during validation. Available defaults are validated,
/// unavailable optional values are skipped, and help/completion bypass these callbacks.
/// Use built-in attributes for simple bounds or allowed sets. Use a converter for a
/// different text format, or <c>[CliCrossValidator]</c> for a relationship between properties.
/// </para>
/// </remarks>
/// <example>
/// Reject reserved tags and point at the first character of the rejected item:
/// <code language="csharp"><![CDATA[
/// using System;
/// using System.Collections.Generic;
/// using Mauve.Cli.Declarations;
/// using Mauve.Cli.Definitions;
/// using Mauve.Cli.Validation;
///
/// [CliCommand("tool", IsRoot = true)]
/// public partial class ToolCommand
/// {
///     [CliOption("tag", Arity = OptionArity.RepeatedSingleCollection)]
///     [CliValidator<TagValidator>(Target = CliValidationTarget.Items)]
///     public required IReadOnlyList<string> Tags { get; init; }
/// }
///
/// public sealed class TagValidator : ICliValidator<string>
/// {
///     public static ValueValidationOutcome Validate(string? value, ValidationContext context)
///     {
///         return value?.StartsWith("sys-", StringComparison.Ordinal) == true
///             ? ValueValidationOutcome.FailureAt("tag.reserved", 0,
///                 message: "Tags starting with 'sys-' are reserved.")
///             : ValueValidationOutcome.Success;
///     }
/// }
/// ]]></code>
/// To reject by exception instead, throw
/// <c>new CliValidationException(ValueValidationOutcome.Failure("tag.reserved", message: "Choose a nonreserved tag."))</c>.
/// Supply the public reason on the outcome, not just as an exception message.
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class CliValidatorAttribute<TProvider> : Attribute
{
    /// <summary>Gets or sets whether the provider receives the complete property value or each collection item. The default is <c>Value</c>.</summary>
    /// <remarks>
    /// Changing the target also changes the required <c>ICliValidator&lt;T&gt;</c>
    /// type. Items checks each item in order, preserves the first rejection for this rule,
    /// and does not run the provider for an empty collection.
    /// </remarks>
    public CliValidationTarget Target { get; set; }
}

/// <summary>Checks a relationship between several bound properties before the selected command handler runs.</summary>
/// <remarks>
/// <para>
/// Put this attribute on a static, nongeneric method declared on the command class.
/// The method must synchronously return <c>ValueValidationOutcome</c>.
/// Parameters bind to the command's option or argument properties, including options
/// in its constructor-included packs and nested packs. Parameter names match property
/// names without regard to case, and types must match exactly, including nullability.
/// These are C# property names, not option spellings: a property named <c>Mode</c>
/// still binds to <c>mode</c> when its option is named <c>--execution-mode</c>.
/// </para>
/// <para>
/// Every parameter must have exactly one matching property. Ambiguous names/types,
/// missing matches and <c>ref</c>, <c>in</c> or <c>out</c> parameters are errors.
/// Declare the method on the command, not on the option-pack class; pack properties
/// can be inputs but pack-level methods are not registered by this attribute.
/// Ancestor command properties are not automatically parameter candidates.
/// </para>
/// <para>
/// The method runs only after conversion, requiredness, defaults and all property
/// validation have succeeded. It receives effective values, including defaults and
/// environment values, not a newly constructed command object. If a bound optional value
/// is unavailable, the generated callback skips the entire method. Available null is
/// different: the method can receive it and must handle it. Use required input or an
/// explicit-model context-based validator if skipping does not fit your presence rule.
/// </para>
/// <para>
/// Return success or a structured rejection. <c>FailureForParameter</c> targets a
/// zero-based method parameter index, so the resulting diagnostic points at that input.
/// It can also specify a zero-based item index and a UTF-16 offset within that item.
/// An untargeted <c>Failure</c> reports a command-level problem without inventing a token.
/// You can also throw <c>CliValidationException</c> carrying the same outcome.
/// Other exceptions propagate as application faults.
/// </para>
/// <para>
/// Supply an actionable public <c>message</c> as well as a stable failure code.
/// Messages are bounded and sanitized, and are suppressed for sensitive or hidden targets;
/// never include secrets or raw input, especially in untargeted cross-value messages.
/// Multiple methods run in source order and returned failures can accumulate up to the
/// diagnostic limit. A failed earlier stage prevents cross-validation; help and completion
/// do not execute it. Use property validation attributes instead when one property's
/// value alone determines whether it is valid.
/// </para>
/// </remarks>
/// <example>
/// Point at the mode option when fast mode is combined with an excessive limit:
/// <code language="csharp"><![CDATA[
/// using Mauve.Cli.Declarations;
/// using Mauve.Cli.Validation;
///
/// [CliCommand("tool", IsRoot = true)]
/// public partial class ToolCommand
/// {
///     [CliOption]
///     [CliRange(1, 50)]
///     public int Limit { get; init; } = 10;
///
///     [CliOption]
///     [CliAllowedValues("safe", "fast")]
///     public string Mode { get; init; } = "safe";
///
///     [CliCrossValidator]
///     private static ValueValidationOutcome CheckMode(int limit, string mode)
///     {
///         return mode == "fast" && limit > 20
///             ? ValueValidationOutcome.FailureForParameter("mode.limit", 1,
///                 message: "Fast mode requires --limit to be at most 20.")
///             : ValueValidationOutcome.Success;
///     }
/// }
/// ]]></code>
/// Parameter 1 is <c>mode</c>, so <c>--mode fast --limit 21</c> locates the
/// rejection at the token supplying <c>fast</c>. Using index 0 would point at
/// the limit instead. For a failure of the overall combination without one preferred
/// input, return <c>ValueValidationOutcome.Failure("mode.limit", message: "Fast mode requires a smaller limit.")</c>.
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CliCrossValidatorAttribute : Attribute
{
}
