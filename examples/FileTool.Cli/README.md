# FileTool.Cli scaffold

This .NET 11 executable currently prints an explicit not-implemented message.
It is wired to the runtime and build-time generator projects and can be
published as NativeAOT. It performs no parsing or file operations.

The future example should demonstrate a `copy` command, repeated/rest path
arguments, `--` for dash-prefixed filenames, composed OptionPacks, global
options and completion. No overwrite behavior or filesystem side effects
are part of this bootstrap. See the
[planned requirements](../../docs/Requirements/Planned/README.md).
