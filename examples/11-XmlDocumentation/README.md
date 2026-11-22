# 11 - XML documentation and help examples

The declarative application derives help metadata from XML comments. The manual
application builds the equivalent metadata with descriptions and `AddExample`.
Neither writes todo data or contacts services.

```sh
dotnet run --project examples/11-XmlDocumentation/Declarative -- --help
dotnet run --project examples/11-XmlDocumentation/Declarative -- item add --help
dotnet run --project examples/11-XmlDocumentation/Manual -- item add --help
dotnet run --project examples/11-XmlDocumentation/Declarative -- item add --text "Buy milk" work
```

Command `<summary>` and `<remarks>` become short and long descriptions.
Option and positional `<summary>` becomes their description. Pack `<summary>`
becomes its title and `<remarks>` its description. Explicit attribute arguments
win, including empty text or null; `--dry-run` therefore displays
`Attribute description wins.` rather than its XML description.

One invocation per `<example><code>...</code></example>` is a shell example by
default. `language="shell"` states this explicitly. Other languages, including
the example's `language="csharp"` block, stay in XML documentation and do not
appear in help. Multiline C# blocks are fine; shell blocks contain one
nonempty line, optionally surrounded by documentation indentation.

The nested add command's help contains:

```text
Usage: todo item add [OPTIONS] [category]
Add a todo item.
Attribute metadata overrides XML text when both are present.
Examples:
  todo item add --text AAA
  <actual-executable> item add --text "Buy milk" work
```

The `add` prefix acquires the missing canonical ancestors automatically.
Standalone `$0` is only the binary name from process argv zero, not a declared
application command. The second example is authored as
`$0 add --text "Buy milk" work`; expansion inserts `item` between the binary
and `add`. The actual executable value depends on the launch method.
`CliHostOptions(executableName: "todo-cli")` provides an explicit display name;
direct `HelpDocument.Create` uses the root label as its fallback.

Existing OptionPack behavior is unchanged: the Output group appears in root
help, and its explicitly inherited `--verbose` option also appears in child
help. Examples are inert display text, not executable shell scripts.
