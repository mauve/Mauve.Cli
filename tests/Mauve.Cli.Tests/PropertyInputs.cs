using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mauve.Cli.Tests;

internal static class PropertyInputs
{
    private const int _iterationCount = 64;
    private const int _maximumUtf16Length = 32;
    private const int _maximumCompletionTokenLength = 24;
    private const uint _utf16Seed = 0x5EED_1601;
    private const uint _completionSeed = 0x5EED_1005;
    private const uint _csvSeed = 0x5EED_1603;

    private static readonly string[] _utf16Examples =
    [
        string.Empty,
        "\0",
        "\u0001\u001f\u007f",
        "\ud83d\ude00",
        "\ud800",
        "\udfff"
    ];

    private static readonly CompletionToken[] _completionExamples =
    [
        new("plain", CompletionEncodingKind.UnquotedEscapes),
        new("two words", CompletionEncodingKind.DoubleQuoted),
        new("single ' quote", CompletionEncodingKind.SingleQuoted),
        new("unfinished value", CompletionEncodingKind.UnmatchedDoubleQuote),
        new("unfinished ' value", CompletionEncodingKind.UnmatchedSingleQuote),
        new("double \" quote and \\ slash", CompletionEncodingKind.UnquotedEscapes)
    ];

    private static readonly string[][] _csvExamples =
    [
        [string.Empty],
        [string.Empty, "middle", string.Empty],
        ["a,b", "c\"d"],
        [string.Empty, string.Empty, string.Empty],
        ["plain", "two words"]
    ];

    private static readonly char[] _completionAlphabet =
    [
        'a',
        'b',
        'x',
        '7',
        ' ',
        '\t',
        '\'',
        '"',
        '\\',
        'é',
        '\ud83d',
        '\ude00'
    ];

    private static readonly char[] _csvAlphabet =
    [
        'a',
        'b',
        'x',
        '7',
        ' ',
        ',',
        '"'
    ];

    public static IEnumerable<GeneratedCase<string>> ArbitraryUtf16Strings()
    {
        DeterministicRandom random = new(_utf16Seed);

        for (int iteration = 0; iteration < _iterationCount; iteration++)
        {
            string value;
            if (iteration < _utf16Examples.Length)
            {
                value = _utf16Examples[iteration];
            }
            else
            {
                int length = random.Next(_maximumUtf16Length + 1);
                StringBuilder builder = new(length);
                for (int index = 0; index < length; index++)
                {
                    builder.Append((char)random.Next(ushort.MaxValue + 1));
                }

                value = builder.ToString();
            }

            yield return new GeneratedCase<string>(
                "ArbitraryUtf16",
                _utf16Seed,
                iteration,
                value);
        }
    }

    public static IEnumerable<GeneratedCase<CompletionToken>> CompletionTokenEncodings()
    {
        DeterministicRandom random = new(_completionSeed);

        for (int iteration = 0; iteration < _iterationCount; iteration++)
        {
            CompletionToken token;
            if (iteration < _completionExamples.Length)
            {
                token = _completionExamples[iteration];
            }
            else
            {
                int length = random.Next(_maximumCompletionTokenLength + 1);
                StringBuilder logicalToken = new(length);
                for (int index = 0; index < length; index++)
                {
                    logicalToken.Append(_completionAlphabet[random.Next(_completionAlphabet.Length)]);
                }

                CompletionEncodingKind kind = (CompletionEncodingKind)(iteration % 5);
                token = new CompletionToken(logicalToken.ToString(), kind);
            }

            yield return new GeneratedCase<CompletionToken>(
                "CompletionTokenEncoding",
                _completionSeed,
                iteration,
                token with { EncodedToken = EncodeCompletionToken(token.LogicalToken, token.Kind) });
        }
    }

    public static IEnumerable<GeneratedCase<CsvInput>> EnvironmentCsvValues()
    {
        DeterministicRandom random = new(_csvSeed);

        for (int iteration = 0; iteration < _iterationCount; iteration++)
        {
            string[] fields;
            if (iteration < _csvExamples.Length)
            {
                fields = _csvExamples[iteration];
            }
            else
            {
                fields = new string[random.Next(4) + 1];
                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    int length = random.Next(13);
                    StringBuilder field = new(length);
                    for (int index = 0; index < length; index++)
                    {
                        field.Append(_csvAlphabet[random.Next(_csvAlphabet.Length)]);
                    }

                    fields[fieldIndex] = field.ToString();
                }
            }

            string[] fieldCopy = (string[])fields.Clone();
            CsvInput input = new(Array.AsReadOnly(fieldCopy), EncodeEnvironmentCsv(fieldCopy));
            yield return new GeneratedCase<CsvInput>(
                "EnvironmentCsv",
                _csvSeed,
                iteration,
                input);
        }
    }

    public static string EncodeEnvironmentCsv(IReadOnlyList<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        StringBuilder encoded = new();
        for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            string field = fields[fieldIndex];
            ArgumentNullException.ThrowIfNull(field);

            if (fieldIndex > 0)
            {
                encoded.Append(',');
            }

            encoded.Append('"');
            foreach (char character in field)
            {
                if (character == '"')
                {
                    encoded.Append('"');
                }

                encoded.Append(character);
            }

            encoded.Append('"');
        }

        return encoded.ToString();
    }

    private static string EncodeCompletionToken(
        string logicalToken,
        CompletionEncodingKind kind)
    {
        StringBuilder encoded = new();
        switch (kind)
        {
            case CompletionEncodingKind.UnquotedEscapes:
                foreach (char character in logicalToken)
                {
                    if (char.IsWhiteSpace(character) || character is '\'' or '"' or '\\')
                    {
                        encoded.Append('\\');
                    }

                    encoded.Append(character);
                }

                break;
            case CompletionEncodingKind.SingleQuoted:
            case CompletionEncodingKind.UnmatchedSingleQuote:
                encoded.Append('\'');
                foreach (char character in logicalToken)
                {
                    if (character == '\'')
                    {
                        encoded.Append("'\\''");
                    }
                    else
                    {
                        encoded.Append(character);
                    }
                }

                if (kind == CompletionEncodingKind.SingleQuoted)
                {
                    encoded.Append('\'');
                }

                break;
            case CompletionEncodingKind.DoubleQuoted:
            case CompletionEncodingKind.UnmatchedDoubleQuote:
                encoded.Append('"');
                foreach (char character in logicalToken)
                {
                    if (character is '"' or '\\')
                    {
                        encoded.Append('\\');
                    }

                    encoded.Append(character);
                }

                if (kind == CompletionEncodingKind.DoubleQuoted)
                {
                    encoded.Append('"');
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported completion encoding.");
        }

        return encoded.ToString();
    }

    internal readonly record struct CompletionToken(
        string LogicalToken,
        CompletionEncodingKind Kind,
        string EncodedToken = "");

    internal readonly record struct CsvInput(
        IReadOnlyList<string> Fields,
        string EncodedValue);

    internal readonly record struct GeneratedCase<T>(
        string SeedName,
        uint Seed,
        int Iteration,
        T Value)
    {
        public string AssertionMessage =>
            $"{SeedName} seed 0x{Seed.ToString("X8", CultureInfo.InvariantCulture)}, iteration {Iteration}";
    }

    internal enum CompletionEncodingKind
    {
        UnquotedEscapes,
        SingleQuoted,
        UnmatchedSingleQuote,
        DoubleQuoted,
        UnmatchedDoubleQuote
    }

    private struct DeterministicRandom(uint seed)
    {
        private uint _state = seed == 0 ? 1 : seed;

        public int Next(int exclusiveMaximum) => (int)(NextUInt32() % (uint)exclusiveMaximum);

        private uint NextUInt32()
        {
            uint value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }
    }
}
