# Examples

This directory contains small command-line applications that show how to use
Mauve.Cli.

- `Todo.Cli` uses generated command declarations and demonstrates nested
  commands and generated handler binding.
- `FileTool.Cli` builds a command model directly and demonstrates options,
  positionals, and invocation.

Both apps print the requested action; they do not save tasks or change files.
The AOT verification scenario currently shares code with these apps so the
same parser behavior can be published and run with trimming and NativeAOT.
That scenario is a test fixture, not a user-facing example.
