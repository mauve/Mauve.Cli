# Custom converters

Both applications use statically referenced providers implementing
`ICliValueConverter<T>`. They only print converted values; they perform no
filesystem or network operations.

## Run both versions

Run these complete commands from the repository root:

```sh
for variant in Declarative Manual; do
  dotnet run --project "examples/13-CustomConverters/$variant" -- \
    '[audit:B-7' '0x0F]' --code B-42 --mask 0xFF --count 10 B-9
done
```

Both versions print:

```text
audit: code=7; mask=15
code=42; mask=255; count=10; input=9
```

The brackets delimit a directive span. The colon separates its name from
the values; subsequent values are separate argv tokens, not colon-separated.
Here the shell quotes protect the opening and closing bracket tokens.
The audit handler runs before the command handler.

Run without the directive:

```sh
dotnet run --project examples/13-CustomConverters/Declarative -- \
  --code B-42 --mask 0xFF --count 10 B-9
dotnet run --project examples/13-CustomConverters/Manual -- \
  --code B-42 --mask 0xFF --count 10 B-9
```

## Converter ownership

| Value | Declarative registration | Explicit registration |
| --- | --- | --- |
| `--code`, positional `input`, audit `code` | `[CliConverter<BuildCodeConverter>(CompletionSafe = true)]` on `BuildCode` automatically registers the type in the generated root. | `root.RegisterConverter<BuildCode, BuildCodeConverter>(completionSafe: true)` registers the model-wide provider. |
| `--mask`, audit `mask` | `[CliConverter<HexIntConverter>(CompletionSafe = true)]` on the bound property overrides just that definition. | `ValueConverterRegistration.Create<int, HexIntConverter>(completionSafe: true)` is passed to the option or directive value definition. |
| `--count` | No override; uses built-in decimal `int` parsing. | No override; uses the same built-in parsing. |

Standalone declarative directive properties use `[CliOption]` to declare
ordered bare directive values; they do not become ordinary `--options`.
The custom positional is declared with `[CliArgument]` on the command.
The manual model uses `PositionalDefinition` and `DirectiveValueDefinition`
with explicit types, and has no source-generator/analyzer project reference.

The providers are readonly structs with static `Convert` methods. No
provider instance, reflective method lookup, or runtime discovery is needed.
They return `ValueConversion.FromValue(...)` on success or a bounded reason
code through `ValueConversion.Failure<T>(...)` on expected invalid input.
Unexpected callback exceptions remain application faults, not parse failures.
`CompletionSafe = true` permits these side-effect-free providers to participate
in completion context conversion; it does not supply completion candidates.

Definition-local overrides take precedence over model registrations.
Optional host registries can be passed in
`CliHostOptions(converters: registry)`, but an exact-type model/host collision
is rejected instead of silently overriding either registration.

## Formats and failures

- `BuildCode`: exact uppercase `B-` prefix followed by a positive decimal
  `int`, without a sign, spaces or grouping separators. `B-42` succeeds;
  `B-0`, `b-42` and `B-bad` fail.
- Hex mask: exact lowercase `0x` prefix followed by hexadecimal digits that
  fit a nonnegative `int`. `0xFF` means 255; `FF`, `0xGG` and `0xFFFFFFFF`
  fail. Hex digits may be uppercase or lowercase.
- Count: an ordinary decimal `int`. The hexadecimal override is local to
  the mask definitions, so `--count 0x10` fails.

Each command below fails with exit code 2, prints a safe diagnostic to
standard error, and runs neither directive nor command handler:

```sh
for variant in Declarative Manual; do
  dotnet run --project "examples/13-CustomConverters/$variant" -- \
    --code B-bad --mask 0xFF --count 10 B-9
  dotnet run --project "examples/13-CustomConverters/$variant" -- \
    --code B-42 --mask 0xGG --count 10 B-9
  dotnet run --project "examples/13-CustomConverters/$variant" -- \
    --code B-42 --mask 0xFF --count 0x10 B-9
  dotnet run --project "examples/13-CustomConverters/$variant" -- \
    --code B-42 --mask 0xFF --count 10 B-bad
  dotnet run --project "examples/13-CustomConverters/$variant" -- \
    '[audit:B-7' '0xGG]' --code B-42 --mask 0xFF --count 10 B-9
done
```

Use `--help` to inspect the options, custom positional and audit directive.

For persistent trimmed managed and NativeAOT publish/run verification of both
pairs, use the [shared verification procedure](../12-BuiltInTypes/README.md#publishrun-verification).
It compares exact command/directive stdout, checks all five custom failure
cases, and records source and compiled-output hashes without reporting these
process smokes as MSTest/Governance test evidence.
