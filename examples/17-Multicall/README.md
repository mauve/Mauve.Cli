# 17 - Multicall binary

Both applications dispatch the same executable through different symlink
names. `multicall` and its alias `suite` select the ordinary root; `farewell`
selects a separate root. The explicit route `smurf -> [multicall, greet]`
starts at a child without declaring that child as another root.

Publish and create symlinks in a dedicated output directory:

```sh
dotnet publish examples/17-Multicall/Declarative -c Release -r linux-x64 \
  -p:PublishAot=false -p:PublishTrimmed=true -p:SelfContained=true \
  -o artifacts/multicall-declarative
cd artifacts/multicall-declarative
ln -s multicall-declarative multicall
ln -s multicall-declarative suite
ln -s multicall-declarative farewell
ln -s multicall-declarative smurf
./multicall greet Ada       # Hello, Ada!
./suite hi Ada             # Hello, Ada!
./farewell Ada             # Goodbye, Ada!
./smurf Ada                # Hello, Ada!
./smurf formal Ada         # Good day, Ada.
./smurf --help             # Usage does not repeat "greet"
```

Run the manual counterpart by replacing `Declarative` with `Manual`, using
`multicall-manual` as the symlink target, and choosing a different output
directory. Set `PublishAot=true` for NativeAOT publication.
Both versions have the same command and route behavior and only print output.

`DispatchMulticallAsync` accepts validated models, optional immutable
`CliMulticallRoute` entries, argument tokens excluding the executable, and
ordinary `CliHostOptions`. The executable is
`CliHostOptions.ExecutableName`, defaulting to process argv zero. Do not use
the resolved executable path: it loses the symlink's name. The example also
registers its original apphost filename as a root alias, so launching that
filename directly works; `dotnet run` is not a symlink verification.

Routes use full command paths and can contain declared aliases. They never
prepend fake command tokens. Root directives, converters, inherited options,
environment prefixes, and caller argument coordinates remain unchanged.
Executable names use exact ordinal matching on Unix; Windows ignores casing
and a trailing `.exe`. Command path segments always use ordinal matching.
Invalid paths, duplicate normalized executable names, and unmatched invocation
names throw before any directive or handler runs. There is no fallback root.

The repository gate runs this pair through actual symlinks after trimmed and
NativeAOT publication. See [FEAT-17](../../docs/Requirements/FEAT-17-Multicall-Dispatch.md).
