# Fuzzing untrusted input

The deterministic MSTest properties and committed-corpus replay supplement
requirement-oriented examples. Coverage-guided fuzzing is an opt-in activity;
it is not a substitute for tests or an always-on CI gate.

## Prerequisites

The harness uses the repository's .NET SDK, locked NuGet dependencies and the
local tools pinned in `fuzz/.config/dotnet-tools.json` (`SharpFuzz.CommandLine`
for campaigns; `dotnet-coverage` and ReportGenerator for coverage reports).
The runner also requires `jq`, `realpath`, `sha256sum` and GNU-compatible
`timeout`. It requires an existing trusted
[`libfuzzer-dotnet`](https://github.com/Metalnem/libfuzzer-dotnet) executable;
building that native bridge requires Clang and libFuzzer. Review and pin the
bridge source and toolchain used by your environment. The runner does not
download native executables or install global tools.

The procedure targets Linux with a compatible native bridge. Other platforms
require their own verified bridge and supporting tools; source presence alone
is not platform evidence.

## Replay and campaigns

To replay all committed seeds without mutation after a locked Release build:

```sh
dotnet restore Mauve.Cli.slnx --locked-mode
dotnet build Mauve.Cli.slnx -c Release --no-restore
bash scripts/replay-fuzz-corpus.sh all
```

For a bounded campaign, provide a bridge you have built or otherwise verified:

```sh
bash scripts/fuzz.sh completion --seconds 60 \
  --bridge /path/to/libfuzzer-dotnet \
  --output artifacts/fuzzing/completion-example
```

Targets are `completion`, `shell`, `csv` and `argv`. `--seconds` accepts
1-86400 seconds and defaults to 60. `--corpus DIR` selects a caller-supplied
read-only seed directory; otherwise the committed target corpus is used.
`--output DIR` must name a new directory under `artifacts/fuzzing/`. The
runner creates a separate writable generated corpus and findings directory,
publishes isolated copies, and instruments only the published product
assembly (`Mauve.Cli.dll`); instrumenting the harness assembly as well crashed
the bridge at startup with `AccessViolationException` in a live run. It bounds each
input and enforces a campaign watchdog. It never automatically adds generated
inputs to the committed corpus.

The four targets use bounded binary inputs and public-API models:

| Target | Boundary |
| --- | --- |
| `completion` | Raw command-line tokenization and UTF-16 cursor mapping, then `CompletionService` (directives, short clusters, positionals, providers, cancellation) against the rich model. |
| `shell` | Shell protocol validation and candidate encoding; no shell script is executed. |
| `csv` | Environment collection binding with raw or independently encoded CSV; with selector bit 7 set, 14 typed environment variables (integers, enum, decimal, collections, validators, a sensitive value) bound against the rich model. |
| `argv` | Argument, directive and option parsing, invoker dispatch, result isolation and string preservation against the rich model. |

The rich model (`fuzz/Mauve.Cli.Fuzzing/RichModel.cs`) uses only public
APIs: an enum with an explicit map, integer/long/byte/decimal/double options,
defaults, ranges, allowed values, string and collection length validators, a
sensitive option, counting and negatable flags, repeated and greedy collections,
three directives, a nested child command with required options and collection
positionals, and completion providers.

Inputs use byte selectors and length-prefixed UTF-16LE code units. Missing
bytes are zero; text is limited to 64 code units per field and incomplete
trailing code units are ignored. For the `completion`, `argv` and `csv`
argument tokens, a selector byte of 192-255 picks one of 64 fixed grammar
tokens (options, commands, numbers, modes, directives) instead of starting
text; the palette in `Program.cs` is append-only so committed inputs keep their
meaning. The harness limits input processing; the campaign runner further caps
generated inputs at 4096 bytes. Corpora and libFuzzer dictionaries are kept
under `fuzz/Mauve.Cli.Fuzzing/corpus/` and `fuzz/Mauve.Cli.Fuzzing/dictionaries/`.

The bridge reports `cov:` for its own few counters; the `ft:` (features) value
is the meaningful progress signal because SharpFuzz coverage reaches libFuzzer
through extra counters.

## Corpus ownership and promotion

Hand-written seeds have descriptive names. Files prefixed `gen-` were produced
by campaigns and promoted after `libFuzzer -merge=1` minimization against the
committed seeds, so each adds coverage features. Regression seeds
(`regression-*`) are crash inputs for fixed defects. To promote more inputs:

1. Run a campaign and merge its corpus into an empty directory with
   `-merge=1 <new> <committed corpus> <campaign corpus>` using the same bridge,
   harness publish directory and `MAUVE_FUZZ_TARGET`.
2. Copy only files whose content is not already committed, prefixed `gen-`.
3. Run `bash scripts/replay-fuzz-corpus.sh all`; every input must replay clean.
4. Measure with `scripts/fuzz-coverage.sh` and review the diff as code.

## Coverage

```sh
bash scripts/fuzz-coverage.sh                       # all targets, committed corpus
bash scripts/fuzz-coverage.sh --target argv \
  --extra-corpus argv=artifacts/fuzzing/<run>/corpus   # add a read-only corpus
```

The script restores the pinned tools, publishes the harness, replays each
target's corpus (single process per target) under `dotnet-coverage`, and writes
Cobertura XML plus HTML/`Summary.txt` reports filtered to the `Mauve.Cli`
assembly under a new `artifacts/coverage/fuzz-<timestamp>-<pid>/` directory
(`--output DIR` must be new and beneath `artifacts/`). `environment.txt` records
commit, tool versions and corpus hashes. Coverage is the whole assembly, so it
includes code that no fuzz target is meant to reach; it measures what the
inputs execute, not defect absence. It is a reporting tool, not a pass/fail gate.

## Triage and evidence

Record target, commit, package/tool and bridge versions and hashes, corpus
identity, duration, platform and actual outcome for each run. The runner
records environment and input hashes alongside its log under the run directory.
A build, a committed seed or a coverage report is not evidence that a
coverage-guided run passed.

For an accepted finding, retain a minimized artifact outside source control
while triaging, add a focused requirement-tagged MSTest regression, then add a
minimal corpus seed when it improves path discovery. Review corpus and
dictionary changes as code; never commit secrets or unreviewed generated data.
Scheduled or hosted fuzzing requires a separate decision on runner, budget,
artifact retention and triage ownership.
