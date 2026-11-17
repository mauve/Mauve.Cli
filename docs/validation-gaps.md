# Validation gaps and requirement evidence

This editable ledger starts from a Mauve.Governance template; add a row for
**every active requirement** in `docs/Requirements/`, even when all tests pass.
It is not manifest-owned and updates must not overwrite your evidence. See the
[original Mauve.Governance Policy](https://github.com/mauve/Mauve.Governance/blob/main/src/templates/docs/governance.md)
and the installed [policy](../governance.md).

## Bootstrap baseline

There are **zero active parser requirements and zero product tests**.
All parser requirements are in `Requirements/Planned/`; they are not passing
coverage and do not receive fabricated active ledger rows.

The standalone runner/analyzer/TRX probe verifies only infrastructure.
Its synthetic feature is intentionally outside this repository's product
inventory and is excluded from source traceability. Native example runs
verify scaffold/toolchain compatibility only. See
[POL-01](Policies/POL-01-Strict-Dotnet-And-Aot.md) for executable local gates.
Hosted/protected CI and real parser acceptance evidence remain unavailable.

The bootstrap's complete local `bash scripts/verify.sh` gate has been executed:
strict Governance validation, locked restore, zero-warning Release build,
format verification, one passing isolated infrastructure observation, expected
MGTEST001/IDE0161 rejections, and both native scaffold executions succeeded.
This observation is limited to Linux ARM64/.NET 11 with the .NET 10 tooling
runtime alongside it. Local logs are in `artifacts/verification.log`; the
infra TRX/import are under `artifacts/obj/governance-probe/`. These are not
active parser requirement evidence or protected hosted provenance.

## Active requirement methods

| Requirement | Validation method | Evidence or procedure | Owner | Environment / prerequisites | Gap or waiver details |
| --- | --- | --- | --- | --- | --- |

Use one row per method when a requirement has multiple methods. Classify each
as **missing**, **manual**, **portable automated**, **environment-gated
automated**, or **waived**. Link the test or manual procedure, name gated
prerequisites, and record the actual evidence separately from the intended
method. A waiver needs an approver, owner, reason and expiry; it is not a pass.
a missing or unavailable test is a gap, not an observed success. An
alternative to a C# source test needs a repository-local procedure file
in the Evidence or procedure column. The source declaration gate validates
the ledger but does not merge it with manual or TRX run reports.
For a waiver, record `approver: Name; reason: Explanation; expiry: YYYY-MM-DD`
in Gap or waiver details, and an Owner in its own column. A missing-only
row fails the declaration gate.
