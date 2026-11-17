# ADR-002: Local-first Governance enrollment

## Status

Accepted for bootstrap. Published/hosted distribution remains unverified.

## Context

Mauve.Governance 0.14.0 supports managed-asset enrollment and strict planned
requirements validation, but documents only .NET 10 consumer verification.
There is no verified public package release. Its checkout is separate from
this repository and may contain local changes; its Git revision alone is
not the complete identity of those changes.

## Decision

Use the supported `core-requirements` profile. Keep the tool manifest and
all other manifest-owned copies unchanged. Custom instructions, strict
configuration, ADRs, feature documents and feedback are consumer-owned.

Build the documented Governance packages from a trusted configurable checkout
under its own .NET 10 SDK. Store its local feed, private tool and NuGet cache
under ignored target artifacts. Use source mapping so Governance packages
cannot silently resolve from public sources. Pin bundle/packages to 0.14.0.
Restore the generated local tool manifest with the .NET 11 consumer SDK.

Treat source/package changes under the same unpublished version as an explicit
upgrade/reproducibility decision, not a way to silently replace cached code.
The bootstrap records source state and package hashes. If newly packed bytes
conflict with this checkout's cached package, stop and use a reviewed source/
version and fresh isolated target cache rather than ignoring hash mismatches.
Never edit a sibling checkout's authored files to make enrollment pass.

Separately test the net11 MSTest adapter, analyzer and VSTest TRX logger with
an isolated runner probe. Do not mix synthetic probe metadata with
[planned parser requirements](../Requirements/Planned/README.md), source
traceability or coverage.

## Consequences

The local source snapshot/feed is an external restore prerequisite. It is
not available to a fresh hosted runner unless explicitly provisioned.
Managed hosted workflows stay disabled; their existence is not CI evidence.
Any future hosted workflow must install the pinned product SDK as well as
Governance's runtime and use a verified feed/source snapshot.

The recorded local .NET 11 compatibility observations belong in
[governance-feedback.md](../governance-feedback.md). This decision does not
promise every SDK, runner platform or future Governance version works.
