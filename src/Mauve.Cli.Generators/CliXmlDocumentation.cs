using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mauve.Cli.Generators;

internal sealed class CliXmlDocumentation(string? summary, string? remarks, string[] examples, bool isValid)
{
    public string? Summary { get; } = summary;

    public string? Remarks { get; } = remarks;

    public string[] Examples { get; } = examples;

    public bool IsValid { get; } = isValid;

    public static CliXmlDocumentation Read(ISymbol symbol, bool readExamples, CancellationToken cancellationToken)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            SyntaxNode declaration = reference.GetSyntax(cancellationToken);
            if (declaration.GetLeadingTrivia().Any(static trivia =>
                trivia.GetStructure() is DocumentationCommentTriviaSyntax { ContainsDiagnostics: true }))
            {
                return new CliXmlDocumentation(null, null, [], false);
            }
        }

        string xml = symbol.GetDocumentationCommentXml(expandIncludes: false, cancellationToken: cancellationToken) ?? string.Empty;
        if (xml.Length == 0)
        {
            return new CliXmlDocumentation(null, null, [], true);
        }

        XElement document;
        try
        {
            using StringReader input = new(xml);
            using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            document = XElement.Load(reader);
        }
        catch (XmlException)
        {
            return new CliXmlDocumentation(null, null, [], false);
        }

        List<string> examples = [];
        bool valid = true;
        if (readExamples)
        {
            foreach (XElement code in document.Elements("example").SelectMany(static example => example.Elements("code")))
            {
                string? language = (string?)code.Attribute("language");
                if (language is not null && !string.Equals(language, "shell", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = code.Value.Trim();
                if (text.Length == 0 || text.IndexOfAny(['\r', '\n']) >= 0 || code.HasElements)
                {
                    valid = false;
                    continue;
                }

                examples.Add(text);
            }
        }

        return new CliXmlDocumentation(
            GetText(document.Element("summary")),
            GetText(document.Element("remarks")),
            [.. examples],
            valid);
    }

    private static string? GetText(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        StringBuilder text = new();
        AppendText(text, element);
        string[] paragraphs = text.ToString().Split('\n');
        return string.Join("\n", paragraphs.Select(static paragraph =>
            string.Join(" ", paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))).Trim();
    }

    private static void AppendText(StringBuilder text, XElement element)
    {
        foreach (XNode node in element.Nodes())
        {
            if (node is XText literal)
            {
                _ = text.Append(literal.Value.Replace('\r', ' ').Replace('\n', ' '));
            }
            else if (node is XElement child)
            {
                if (child.Name.LocalName == "para")
                {
                    _ = text.Append('\n');
                    AppendText(text, child);
                    _ = text.Append('\n');
                }
                else if (child.Name.LocalName is "see" or "seealso" or "paramref" or "typeparamref")
                {
                    if (child.Value.Length > 0)
                    {
                        AppendText(text, child);
                    }
                    else
                    {
                        string? reference = (string?)child.Attribute("langword")
                            ?? (string?)child.Attribute("name")
                            ?? (string?)child.Attribute("cref");
                        if (reference is not null)
                        {
                            _ = text.Append(reference.Length > 2 && reference[1] == ':' ? reference.Substring(2) : reference);
                        }
                    }
                }
                else
                {
                    AppendText(text, child);
                }
            }
        }
    }
}
