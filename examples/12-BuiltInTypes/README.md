# Built-in types

The paired applications print converted values without contacting the URI or
IP address, checking path existence, or creating files or directories.
Four child commands keep each invocation manageable:

| Command | Scalar types |
| --- | --- |
| `numbers` | `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `Half`, `float`, `double`, `decimal`, `bool` |
| `identity` | `string`, `char`, `Guid`, `Uri`, `Version`, `IPAddress`, `FileInfo`, `DirectoryInfo`, enum |
| `time` | `DateOnly`, `TimeOnly`, `DateTimeOffset`, `TimeSpan` |
| `shapes` | Nullable `DateOnly`, repeated `Half`, scalar and repeated `PortOrName` union |

## Run both versions

Run from the repository root with .NET 11/C# 15. This shell loop runs the
same complete commands against both projects:

```sh
for variant in Declarative Manual; do
  dotnet run --project "examples/12-BuiltInTypes/$variant" -- numbers \
    --signed-byte -8 --unsigned-byte 8 \
    --signed-short -16 --unsigned-short 16 \
    --signed-int -32 --unsigned-int 32 \
    --signed-long -64 --unsigned-long 64 \
    --half 1.5 --single 2.5 --double 3.5 --decimal 4.5 --enabled true

  dotnet run --project "examples/12-BuiltInTypes/$variant" -- identity \
    --text hello --letter Q --id 01234567-89ab-cdef-0123-456789abcdef \
    --uri https://example.invalid/demo --version 1.2.3 --address 192.0.2.1 \
    --file demo.txt --directory demo-folder --mode Detailed

  dotnet run --project "examples/12-BuiltInTypes/$variant" -- time \
    --date 2026-11-22 --time 16:30:45.123 \
    --timestamp 2026-11-22T16:30:45Z --duration 1.02:03:04

  dotnet run --project "examples/12-BuiltInTypes/$variant" -- shapes \
    --maybe-date --sample 1.5 --sample 2.5 \
    --target 8080 --route 443 --route service
done
```

The time command prints canonical invariant formats. Paths print their
lexical names (`demo.txt`, `demo-folder`), not filesystem metadata.
The shapes command prints:

```text
maybe-date=(null)
sample=1.5
sample=2.5
target=port:8080
route=port:443
route=name:service
```

Nullable options distinguish absent, bare-null and explicit values. An
optional value must use `=`; space-separated values are not consumed:

```sh
for variant in Declarative Manual; do
  dotnet run --project "examples/12-BuiltInTypes/$variant" -- shapes \
    --sample 1.5 --target service --route 443
  dotnet run --project "examples/12-BuiltInTypes/$variant" -- shapes \
    --maybe-date=2026-11-22 --sample 1.5 --target service --route 443
done
```

The first prints `maybe-date=(absent)` and the second prints
`maybe-date=2026-11-22`. Both handlers use `CommandInvocationContext` and
`TryGetOptionValue` to preserve this distinction.

## Accepted formats

| Type | Input |
| --- | --- |
| `string` | One complete token; shell quoting can retain spaces. |
| `bool` | `true` or `false`; a bare `--enabled` means true. |
| Fixed-width integers | Signed or unsigned decimal text within the target range; no separators or surrounding whitespace. |
| `Half`, `float`, `double` | Invariant decimal/exponent text; NaN, infinities and overflow are rejected. |
| `decimal` | Invariant decimal text without grouping separators; overflow is rejected. |
| `char` | Exactly one non-surrogate UTF-16 code unit. |
| `Guid` | Hyphenated `D` form, as in the command above. |
| `Uri` | A well-formed absolute URI; conversion makes no request. |
| `Version` | Two to four unsigned ASCII decimal components. |
| `IPAddress` | Strict four-component decimal IPv4 or valid IPv6, for example `2001:db8::1`; no legacy abbreviated, octal or hexadecimal IPv4. |
| `FileInfo`, `DirectoryInfo` | Nonempty lexical paths; no existence, accessibility, kind or authorization guarantee. |
| Enum | Exact ordinal names `Compact` or `Detailed`; not numeric values or lowercase spellings. |
| `DateOnly` | Exact `yyyy-MM-dd`. |
| `TimeOnly` | `HH:mm`, `HH:mm:ss`, or `HH:mm:ss` with one to seven fractional digits. |
| `DateTimeOffset` | ISO date/time with seconds and explicit `Z` or numeric offset, optionally one to seven fractional digits. |
| `TimeSpan` | Invariant constant (`c`) format, such as `1.02:03:04`. |

Numeric conversion defaults to invariant culture; hosts can opt into a
different numeric culture through `CliHostOptions(numericCulture: culture)`.
Temporal formats remain invariant.

`PortOrName` is a C# 15 nominal union declared with `int` before `string`.
First successful case conversion wins: `8080` is an integer, while `service`
falls through to string. Catch-all string cases should come last. The
declarative generator emits the case constructors and enum map; the manual
project supplies `RegisterUnion` constructors and an explicit
`RegisterEnum` name/value map through `AddConverters`, without reflection
or an analyzer reference.

The union declaration alone has a `NET11_0_OR_GREATER` target-feature guard
for compatibility with the older Governance source-only parser. These projects
always target .NET 11, so normal builds compile the modern union and all
handlers and properties unconditionally.

## Rejected values

These complete commands fail with exit code 2 and do not run the handler:

```sh
for variant in Declarative Manual; do
  dotnet run --project "examples/12-BuiltInTypes/$variant" -- time \
    --date 2026-02-30 --time 16:30 \
    --timestamp 2026-11-22T16:30:45Z --duration 00:01:00
done
```

Use `--help` or `time --help` for generated or explicit help.

## Publish/run verification

On a native Linux x64 host with .NET 11 and Clang, run:

```sh
bash scripts/verify-conversion-examples.sh artifacts/conversion-example-verification clang
```

This also verifies the CustomConverters pair: locked restore, strict Release
builds, separate trimmed managed and NativeAOT publications, exact output
comparisons for every scalar and representative shapes, and rejected input.
The fresh `conversion-examples` subdirectory retains command logs, stdout,
stderr, expected output, environment details and source/output SHA-256 hashes.
Existing evidence is never overwritten or deleted. Omit `clang` to use the
default compiler; append `--managed-smoke` for builds and bounded managed
execution only, without claiming trimmed/native verification.
These process smokes are not MSTest or Governance test-run acceptance evidence.
