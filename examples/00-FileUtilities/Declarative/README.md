# 00 - File utilities (declarative)

The generated `fileutils` command copies, inspects and compares files. Copy is
the default behavior; `--dry-run` only previews, and an existing destination
requires `--overwrite`.

The reusable buffer option defaults to `64KiB`, accepts integer byte values
with optional `B`, `KiB` or `MiB` units, and is bounded to `1KiB`–`1MiB`.
`FILEUTILS_BUFFER_SIZE` provides its environment default. A command-line value
wins over the environment value, which wins over the built-in default.
Copy accepts `-n` as an alias for `--dry-run` and `-f` as an alias for
`--overwrite`. The child aliases `cp`, `stat` and `cmp` select `copy`,
`inspect` and `compare`; help uses the canonical names.

```sh
dotnet run --project examples/00-FileUtilities/Declarative -- copy source.bin copy.bin
dotnet run --project examples/00-FileUtilities/Declarative -- copy source.bin copy.bin --dry-run
dotnet run --project examples/00-FileUtilities/Declarative -- inspect source.bin
dotnet run --project examples/00-FileUtilities/Declarative -- compare source.bin copy.bin
dotnet run --project examples/00-FileUtilities/Declarative -- settings
dotnet run --project examples/00-FileUtilities/Declarative -- --help-tree
dotnet run --project examples/00-FileUtilities/Declarative -- '[about]'
dotnet run --project examples/00-FileUtilities/Declarative -- --demo-completion
```

The apphost name `fileutils-declarative` is a root alias, so it also works
with `dotnet run`. Multicall names `mcopy`, `minspect` and `mcompare` route
directly to their respective command paths when launched through the apphost.
The `settings` child prints only the buffer option's origin (`Default`,
`Environment` or `Explicit`), never its value. Completion values for
`--buffer-size` are intentionally bounded to `4KiB`, `64KiB` and `1MiB`.
Mapped file errors use safe messages and exit codes 66 (missing), 77 (access),
and 74 (other I/O or rejected destination).
