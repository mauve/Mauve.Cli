# Examples

This directory contains small command-line applications that show how to use
Mauve.Cli.

- `Todo.Cli` uses generated command declarations and demonstrates nested
  commands and generated handler binding.
- `FileTool.Cli` builds a command model directly and demonstrates options,
  positionals, and invocation.

Both apps print the requested action; they do not save tasks or change files.
Their small `--verify-aot` modes exercise the same models after trimming and
NativeAOT publication. Comprehensive compatibility scenarios live in the
focused test project under `tests/Mauve.Cli.Aot.Tests`.
