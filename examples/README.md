# Examples

Each example contains two small applications: `Declarative` uses generated
command declarations; `Manual` builds the model directly. Each folder has
commands to run both versions.

| Example | What it shows |
| --- | --- |
| [Hello](01-Hello/) | A minimal command and handler |
| [Commands](02-Commands/) | Child commands and aliases |
| [Values](03-Values/) | Options and positional arguments |
| [Value shapes](04-ValueShapes/) | Flags, optional values and collections |
| [Validation](05-Validation/) | Conversion, defaults and validation |
| [Help and errors](06-HelpAndErrors/) | Help, diagnostics and suggestions |
| [Option scope](07-OptionScope/) | Inherited and local options |
| [Option packs](08-OptionPacks/) | Reusable groups of options |
| [Completion](09-Completion/) | Suggestions and shell registration |
| [Directives](10-Directives/) | Custom and predefined directives |
| [XML documentation](11-XmlDocumentation/) | XML-derived help metadata, attribute overrides and invocation examples |
| [Built-in types](12-BuiltInTypes/) | Every built-in scalar, exact enum names, nullable values, collections and C# 15 unions |
| [Custom converters](13-CustomConverters/) | Type-level providers, definition-local overrides, custom positionals and directive values |
| [Short options](14-ShortOptions/) | Combined short options and explicitly counting options |
| [Option sources](15-OptionSources/) | Inspect whether each option came from a default, environment variable or argv |
| [Exception handling](16-ExceptionHandling/) | Direct exceptions, central mappings and a custom renderer with fluent host options |
| [Multicall binary](17-Multicall/) | Symlink-name dispatch to separate roots or an explicitly mapped child command |

These applications print results without changing files or contacting
services. The Commands pair also serves as a representative trimmed and
NativeAOT consumer; broader compatibility scenarios live in
`tests/Mauve.Cli.Aot.Tests`.
