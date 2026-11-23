# 05 - Validation

The declarative and manual projects build equivalent models. Validation runs
after conversion and defaults, before the handler. `--tag` is required;
`--limit`, `--name`, `--mode`, and `--label` have defaults.

Run either project by replacing `Declarative` with `Manual` below:

```sh
dotnet run --project examples/05-Validation/Declarative -- --tag=ok
dotnet run --project examples/05-Validation/Declarative -- --help
dotnet run --project examples/05-Validation/Declarative -- --demo-completion
```

Help lists the allowed `--mode` values; the completion demonstration suggests
`fast` for `--mode=f` without running validators.

| Input after `--` | Demonstrates |
| --- | --- |
| `--tag=ok --limit 99` | Inclusive numeric range; validation error at argument 3 |
| `--tag=ok --start=2027-01-01` | Inclusive temporal range on a nullable date |
| `--tag=ok --limit=bad` | Conversion error, not a validation error |
| `--tag=ok --name=x` | Minimum/maximum UTF-16 string length |
| `--tag=ok --name="  "` | Nonempty/non-whitespace validation |
| `--tag=ok --mode=other` | Ordinal allowed-value set |
| `--tag=aa --tag=bb --tag=cc --tag=dd --tag=ee` | Collection count |
| `--tag=ok --tag=x` | Per-item length; error at argument 2 |
| `--tag=ok --tag=xray` | Custom per-item `CliValidationException`; error at argument 2, UTF-16 offset 7 |
| `--tag=ok --label=blocked` | Custom whole-value validator returning a failure |
| `--tag=ok --mode=fast --limit=21` | Cross-value failure targeting `--mode` at argument 2 |
| `--tag=ok ""` | Nonempty positional argument |

Expected input rejections return `ValidationFailed` diagnostics, including
structured failure identifiers and token coordinates. The default CLI output
explains the rejected constraint, including a one-based item number for
item failures. For example, fast mode with a limit of 21 reports
`Fast mode requires --limit to be at most 20.`, while a one-character tag
reports `String length in UTF-16 code units must be between 2 and 12 (inclusive).`
Custom validators can return failures or throw `CliValidationException`;
other exceptions propagate as application faults rather than being presented
as invalid input.

Supply a public reason separately from the machine-readable failure code:

```csharp
return ValueValidationOutcome.FailureForParameter(
    "mode.limit", 1, message: "Fast mode requires --limit to be at most 20.");
```

The optional `message` is also available on `Failure`, `FailureForItem` and
`FailureAt`, including when the outcome is carried by `CliValidationException`.
Messages are bounded and sanitized for terminal display. Do not include
secrets or raw input; sensitive and hidden targets retain generic diagnostics.
Exception messages are never displayed automatically.
