# Mauve.Governance consumer feedback

## Context and evidence boundaries

Consumer: Mauve.Cli bootstrap, Linux ARM64, SDK 11.0.100/C# 15, with SDK
10.0.404 and the .NET 10 runtime installed alongside it. Governance packages
and bundle: **0.14.0**, built from the sibling checkout at commit
`20587c77c110d0e954d01f74d3eff19dfe9bd4cc` **with pre-existing local changes**.
That commit is not the complete source snapshot identity.

The bootstrap records exact local source status in
`artifacts/governance-source.txt` and package hashes in
`artifacts/governance-packages.sha256`. Generated run evidence is ignored,
not shipped as parser acceptance coverage. No sibling authored changes were
required. Findings below distinguish executed observations from inspection
and unverified environments.

## GF-007: Source traceability parser predates C# 15 union declarations

**State:** observed Tool 0.14.0 compatibility gap. Owner/proposal: Governance
source parser language-version and conditional-compilation support.

**Observed:** `governance validate --root . --strict-planned` reports
`TRACE001: C# source syntax error: Identifier expected` on a C# 15 `union`
declaration, while the pinned .NET 11 compiler compiles it and the behavioral
tests execute it. The Tool's `generate`/`validate` commands expose no parser
version or preprocessor-symbol configuration.

**Consumer compatibility:** guard only the non-test nominal union declarations
with the SDK's `NET11_0_OR_GREATER` symbol. All product/test/example projects
target `net11.0`, so their compiler always includes the real unions. Test
methods, feature/requirement attributes, handlers and assertions are never
guarded or hidden from the source index. The older syntax scanner can then
index every behavioral method without attempting unsupported declaration
syntax. This is not evidence that the Tool understands union semantics.

**Proposal:** update Governance's source Roslyn dependency and support explicit
consumer parse options, including language version and preprocessor symbols.
Keep actual managed/native test evidence independent of syntax-only scanning.
No sibling Governance source, managed asset, analyzer severity, or test
requirement was modified to apply this compatibility guard.

## GF-001: Repacking changes package hashes without changing contents

**State:** confirmed local packaging/reproducibility issue; consumer workaround
implemented. Owner/proposal: Governance packaging/bootstrap documentation.

**Expected:** rebuilding an unchanged pinned package should not invalidate
the consumer's package-content locks merely because the build ran later.

**Observed:** repeating the documented `dotnet pack` sequence rebuilt the Tool
archive with different bytes. Comparing every ZIP entry with the cached
package showed identical entry contents, but newer ZIP timestamps for the
nuspec, relationship/content metadata, `.deps.json` and `.runtimeconfig.json`.
Consumer checksum verification correctly refused to silently substitute it.
The source already sets `Deterministic=true`; that did not fix ZIP timestamps.

**Reproduction:** pack `src/Mauve.Governance.Tool` twice under SDK 10.0.404
with the documented Release/local-feed command, forcing package regeneration
between runs. Compare archive hashes and each entry's content hash/timestamp.
The tool's publish step can regenerate packaging inputs on a repeated run.

**Impact:** locally built packages have unstable content hashes under a fixed
version. Cached restores can hide that a regenerated feed differs; a clean
consumer can then fail its checked-in NuGet locks.

**Workaround/proposal:** pack with an explicit, fixed SDK-supported timestamp
(`-p:DeterministicTimestamp=2000-01-01T00:00:00Z`), in addition to deterministic
compilation. `DeterministicTimestamp=true` was also tried: it assigns the
current time to all entries, so it still changes bytes between packs.
The consumer script applies this to all nine Governance packages and compares
new bytes to the private cache before allowing use. Existing initial
timestamp-bearing bootstrap artifacts/locks were regenerated deliberately.
Add this flag and a repeated-pack/fresh-cache hash check to Governance's
local-first recipe. This does not claim independent clean-host/path
reproducibility, which has not been tested.

**Verification:** repeated packing with the fixed timestamp produced
byte-identical packages against all nine cached Governance packages.
The subsequent locked solution restore and complete `scripts/verify.sh`
gate passed. Logs are retained locally as `artifacts/bootstrap-repeat.log`
and `artifacts/verification.log`.

## GF-002: .NET 11 works for the exercised MSTest/MTP integration

**State:** executed compatibility observation, not an upstream support promise.
Owner/proposal: Governance consumer compatibility matrix.

**Expected:** the bootstrap guide explicitly describes .NET 11 consumers as
unverified; first use should establish rather than assume compatibility.

**Observed:** local tool-manifest restore and strict validation work under the
consumer SDK 11.0.100 while the .NET 10 tool runtime remains installed. A real
`net11.0` MSTest 4.5.1/MTP probe was discovered and passed. Standard MTP TRX
preserved native Feature/Requirement traits, and `governance test-results`
imported one passing infrastructure requirement without the VSTest-only
Governance logger.
The packaged analyzer rejected a separate test missing Feature metadata with
MGTEST001. No Governance source modification or framework downgrade was needed.

**Reproduction:** after `bash scripts/bootstrap-governance.sh <checkout>`,
run `bash scripts/verify-tooling.sh`; inspect its fresh TRX, imported JSON and
`metadata-negative.log` under `artifacts/obj/governance-probe/`.

**Impact/proposal:** document this precise tested SDK/runner combination or add
a net11 fixture to the compatibility suite. This does not verify a .NET
11-only machine, other frameworks/architectures, protected
runner provenance, or every Governance package.

## GF-003: Managed EditorConfig needs a strict-consumer composition recipe

**State:** executed configuration/enforcement friction; not a managed-integrity
bug. Owner/proposal: Governance strict .NET consumer guidance.

**Expected:** a consumer can enforce strict C# style without editing bundle-owned
files or interpreting configured analyzer IDs as installed analyzers.

**Observed:** the root managed config provides newline/encoding and Governance
diagnostic severities, not a strict C# ruleset. We installed the analyzer
packages and added consumer-owned nested configs for source/tests/examples.
A real compiler probe rejects a block namespace with IDE0161; a separate
probe verifies MGTEST001. The managed file and update ownership remain intact.
`GenerateDocumentationFile=true` is needed for build-time IDE0005 enforcement.

**Reproduction:** inspect the nested configs and run
`bash scripts/verify-tooling.sh`; build with
`dotnet build Mauve.Cli.slnx -c Release --no-restore`.

**Impact/proposal:** provide a worked strict-consumer example covering analyzer
references, XML-documentation/build properties, nested config precedence and
format verification. A `.globalconfig` is lower precedence than `.editorconfig`
for conflicting analyzer options, so it is not an interchangeable way to
override every managed setting. Do not prescribe editing the managed root.

## GF-004: Hosted workflow SDK selection is not consumer-SDK-neutral

**State:** source/configuration inspection; no hosted run performed.
Owner/proposal: Governance managed workflow design.

**Expected:** an enabled consumer workflow can provision the consumer's pinned
SDK as well as the Governance runtime.

**Observed:** the installed `governance-policy.yml` sets up SDK 10.0.404,
while this consumer's `global.json` disables roll-forward from SDK 11.0.100.
Local success with both SDKs installed does not establish that the managed
workflow provisions the consumer SDK. The local-only package feed is another
explicit hosted prerequisite.

**Reproduction:** compare `.github/workflows/governance-policy.yml` with
`global.json` and `NuGet.Config`; this is inspection, not an observed hosted
failure.

**Impact/workaround:** leave managed hosted/update opt-ins disabled. Propose
an explicit consumer SDK/feed provisioning hook or a clear multi-SDK consumer
recipe. Do not treat the disabled job as a passing check.

## GF-005: Synthetic tooling sources need deliberate traceability isolation

**State:** source inspection with successful isolation verified locally.
Owner/proposal: Governance source-traversal documentation.

**Expected:** infrastructure probes should not appear as product requirement
declarations or supply apparent coverage for planned behavior.

**Observed:** the traceability walker excludes directories named `bin`/`obj`
but does not generally exclude `artifacts` or consult the consumer's
`.gitignore`. We therefore create the probe under `artifacts/obj/` and keep
checked-in inputs as `.in` files. Strict product validation reports zero
declared tests while the independent runner report records one infrastructure
observation.

**Reproduction:** run both `bash scripts/verify-tooling.sh` and
`dotnet tool run governance -- generate --root . --output artifacts/traceability.json`.
The product index must remain empty of synthetic probe tests.

**Impact/proposal:** document traversal boundaries and an endorsed probe
location, or provide explicit validated exclusion configuration in a future
release. Do not silently skip real product sources by adding arbitrary
exclusions.

## GF-006: `doctor` can stop before reporting adoption capabilities

**State:** observed during SDK-template bootstrap; usability feedback.
Owner/proposal: Governance doctor command.

**Expected:** a diagnostic command should explain installed capabilities even
when the freshly generated consumer is not yet compliant.

**Observed:** between `dotnet new mstest --no-restore` and removing its template
tests, `doctor` failed with MG-GOV-0002/TRACE002 missing method-level Feature
metadata, before printing the capability summary. This correctly rejected
invalid declarations but hid the adoption overview until cleanup.

**Reproduction:** in a disposable enrolled directory, generate an unannotated
MSTest template and run `governance doctor --root <directory>`.

**Impact/proposal:** retain the failing validation result, but also report
capability limits, or document that doctor requires a valid source inventory.
No relaxation of metadata enforcement is requested.
