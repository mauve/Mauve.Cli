# Mauve.Cli

A bootstrapped .NET 11 CLI parser library repository. **No parser or source
generator is implemented yet.** The source-generation-first declarative API
and equivalent explicit model are specified as
[14 planned features](docs/Requirements/Planned/README.md), including
first-class OptionPacks.

## Projects

| Project | Purpose / current state |
| --- | --- |
| `src/Mauve.Cli` | Empty `net11.0` runtime library with AOT compatibility analysis. |
| `src/Mauve.Cli.Generators` | Empty `netstandard2.0` Roslyn component; build-time only. |
| `tests/Mauve.Cli.Tests` | MSTest runtime test scaffold; zero product tests. |
| `tests/Mauve.Cli.Generators.Tests` | MSTest generator test scaffold; zero product tests. |
| `examples/Todo.Cli` | Compiling nested-command example scaffold; no parsing. |
| `examples/FileTool.Cli` | Compiling file/OptionPack example scaffold; no parsing. |

## Bootstrap and verification

Prerequisites: SDK **11.0.100** for this solution, SDK **10.0.404** and the
.NET 10 runtime for the current Governance source/tool, Bash, `jq`, and a
trusted Mauve.Governance **0.14.0** source checkout. Both SDKs must coexist.
Native verification needs a supported native compiler/linker, C development
files and zlib; the recorded baseline is **Linux ARM64 with GCC 15.2 (`cc`)**.
Other platforms/compilers have not been verified.

```sh
bash scripts/bootstrap-governance.sh ../Mauve.Governance
bash scripts/verify.sh
```

The checkout path is configurable. Governance packages are built into the
ignored `artifacts/governance-feed`; NuGet source mapping and a repository-local
package cache prevent accidentally resolving `Mauve.Governance.*` elsewhere.
Do not delete the feed/cache and expect offline restore. Regenerate the feed
from the same trusted source snapshot after cloning. Version `0.14.0` alone
does not identify unpublished local source changes; see
[the enrollment ADR](docs/Architecture/ADR-002-Governance-Enrollment.md).
No globally installed tool is required or modified.
Local packages use deterministic archive timestamps so repeated packaging
does not invalidate lock hashes solely because time passed.

Individual checks, after bootstrapping:

```sh
dotnet tool restore --configfile NuGet.Config
dotnet tool run governance -- validate --root . --strict-planned
dotnet tool run governance -- generate --root . --output artifacts/traceability.json
dotnet restore Mauve.Cli.slnx --locked-mode
dotnet build Mauve.Cli.slnx -c Release --no-restore
dotnet format Mauve.Cli.slnx --verify-no-changes --no-restore
dotnet test Mauve.Cli.slnx -c Release --no-build
bash scripts/verify-tooling.sh
```

`verify.sh` also publishes and executes both NativeAOT scaffold applications
and checks that a Governance update would be a no-op. It uses `cc` explicitly;
set `CPP_COMPILER_AND_LINKER=clang` to select an installed alternative.
The native publish uses the host RID, existing restored assets and previously
built project references, so it does not rewrite shared library lock files.
Changing SDK/RID/dependencies requires deliberate lock regeneration and review.

**Evidence boundaries:** zero discovered product tests is the expected initial
baseline, not passing parser coverage. A separate disposable infrastructure
probe verifies actual MSTest discovery, method-level Governance traits,
metadata-bearing TRX import, and expected metadata/style compiler failures.
Its source lives under ignored `artifacts/obj/`, which Governance excludes;
probe inputs use `.in` extensions and are not product tests. Native execution
proves only the scaffold/toolchain, not future parser or generated bindings.

## Implementation handoff

Start with [agent instructions](docs/AGENT_INSTRUCTIONS.md), the
[feature lifecycle](docs/Requirements/README.md) and
[project boundaries](docs/Architecture/ADR-001-Project-Boundaries.md).
Resolve a feature's marked design blockers before implementing it. Promote
requirements with their implementation, behavioral tests and ledger entries;
do not label remaining planned work as active.

Governance owns the paths recorded in `.mauve-governance/manifest.json`.
Strict C# settings belong in the consumer-owned nested `.editorconfig` files,
not the managed root file. See the
[strict .NET/AOT policy](docs/Policies/POL-01-Strict-Dotnet-And-Aot.md) and
[usage feedback](docs/governance-feedback.md).

The installed hosted Governance workflows remain opt-in and disabled by
default. No hosted package release, protected check or CI execution is
claimed; the reproducible local command is `bash scripts/verify.sh`.
