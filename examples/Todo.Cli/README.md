# Todo.Cli scaffold

This .NET 11 executable currently prints an explicit not-implemented message.
It is wired to the runtime and build-time generator projects and can be
published as NativeAOT. It does not interpret arguments.

The future example should demonstrate `todo add` / `todo list`, positional
text, required/optional and enum options, a reusable output OptionPack, async
execution and cancellation. Implement it only as its
[planned requirements](../../docs/Requirements/Planned/README.md) become active.
Do not use nonexistent API snippets as runnable documentation.
