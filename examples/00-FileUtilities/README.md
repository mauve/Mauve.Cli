# 00 - File utilities

This comprehensive application brings together the CLI features that fit a
small file utility. It is implemented twice: `Declarative` uses generated
command declarations and `Manual` builds an equivalent explicit model.

> **This example performs real file operations.** `copy` writes by default.
> Use `--dry-run` to preview a copy without creating or replacing a file.
> Existing destinations are protected unless `--overwrite` is explicitly
> supplied. Run examples against files you own in a temporary directory.

## Commands

Both versions provide:

- `copy` (`cp`) `SOURCE DESTINATION` to copy a file. Use `-n`/`--dry-run` for a
  no-write preview and `-f`/`--overwrite` to replace an existing destination.
- `inspect` (`stat`) `PATH` to report stable file metadata.
- `compare` (`cmp`) `LEFT RIGHT` to compare file contents.
- `settings` to show the origin of the effective buffer-size option without
  printing its value.
- `--buffer-size SIZE` on copy and compare. The reusable I/O option pack
  accepts byte-size values from 1KiB through 1MiB, such as `64KiB`;
  `FILEUTILS_BUFFER_SIZE` can supply the value through opt-in host
  environment binding.

The host maps expected I/O failures to safe public messages and stable exit
codes. Comparison returns 0 for equal files and 1 when their contents differ.
Unexpected faults are not disguised as successful operations.

## Run

Preview first; remove `--dry-run` only when you intend to write:

```sh
dotnet run --project examples/00-FileUtilities/Declarative -- copy ./source.txt ./copy.txt --dry-run
dotnet run --project examples/00-FileUtilities/Manual -- copy ./source.txt ./copy.txt --dry-run
```

Run the other commands, inspect generated help, and examine option origins
under different value sources:

```sh
dotnet run --project examples/00-FileUtilities/Declarative -- inspect ./source.txt
dotnet run --project examples/00-FileUtilities/Manual -- compare ./source.txt ./copy.txt
dotnet run --project examples/00-FileUtilities/Declarative -- settings
dotnet run --project examples/00-FileUtilities/Declarative -- --help-tree
dotnet run --project examples/00-FileUtilities/Declarative -- --demo-completion
FILEUTILS_BUFFER_SIZE=128KiB dotnet run --project examples/00-FileUtilities/Manual -- settings
dotnet run --project examples/00-FileUtilities/Declarative -- '[completion:bash]'
```

The application also demonstrates a small custom `[about]` directive and the
predefined debugger-wait directive. See `--help` for command-specific options,
defaults, and invocation examples.

## Multicall

The same executable can be invoked through symlink names that start at a
subcommand:

| Executable name | Route |
| --- | --- |
| `mcopy` | `fileutils copy` |
| `minspect` | `fileutils inspect` |
| `mcompare` | `fileutils compare` |

Build or publish either variant, then create symlinks to its apphost in the
same directory. For example, after a self-contained Linux publish:

```sh
dotnet publish examples/00-FileUtilities/Declarative -c Release -r linux-x64 \
  -p:PublishAot=false -p:PublishTrimmed=true -p:SelfContained=true \
  -o artifacts/fileutils-declarative
cd artifacts/fileutils-declarative
ln -s fileutils-declarative mcopy
ln -s fileutils-declarative minspect
ln -s fileutils-declarative mcompare
./mcopy ./source.txt ./copy.txt --dry-run
./minspect ./source.txt
./mcompare ./source.txt ./copy.txt
```

Replace `Declarative` with `Manual` and use the `fileutils-manual` apphost for
the explicit-model variant. The repository verification script exercises
managed, trimmed, and Linux x64 NativeAOT outputs using isolated files and real
symlinks.

## Feature coverage

The pair combines nested commands and aliases, typed positional and option
values, validation and defaults, reusable OptionPacks, short flags, environment
option binding and value origins, a bounded byte-size converter, asynchronous
cancellation-aware handlers, XML-derived
declarative help, explicit manual help metadata, standard help and help trees,
invocation examples, custom and predefined directives, completion, safe
exception mappings, and multicall dispatch.

It intentionally does not repeat inherited/global option scope, collection
and nullable/union value shapes, exhaustive built-in scalar catalogs, every
converter override shape, or the full diagnostics tour. See
[Value shapes](../04-ValueShapes/),
[Built-in types](../12-BuiltInTypes/),
[Custom converters](../13-CustomConverters/),
[Option scope](../07-OptionScope/), [Help and errors](../06-HelpAndErrors/), and
[Multicall](../17-Multicall/) for focused treatments.
