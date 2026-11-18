# Validation gaps and requirement evidence

This editable ledger starts from a Mauve.Governance template; add a row for
**every active requirement** in `docs/Requirements/`, even when all tests pass.
It is not manifest-owned and updates must not overwrite your evidence. See the
[original Mauve.Governance Policy](https://github.com/mauve/Mauve.Governance/blob/main/src/templates/docs/governance.md)
and the installed [policy](../governance.md).

## Current baseline

FEAT-01-001 through FEAT-01-003, FEAT-02-001 through FEAT-02-004,
FEAT-03-001 through FEAT-03-004, FEAT-04-001 through FEAT-04-004, and all of
FEAT-05, FEAT-06, FEAT-07, FEAT-08 and FEAT-09 are active. Portable test methods are
declared for each, but this implementation has no observed product test-run
evidence yet.
Remaining parser requirements are in `Requirements/Planned/`; they are not
passing coverage and do not receive active ledger rows.

The attempted product test run stopped during restore with `NU1301` because
the configured `artifacts/governance-feed` directory is absent; no product
test methods executed. A disposable package-free .NET 11 harness compiled the
parser sources and invoked the declared parser scenarios, but this is not
MSTest or Governance acceptance evidence.

The standalone runner/analyzer/TRX probe verifies only infrastructure.
Its synthetic feature is intentionally outside this repository's product
inventory and is excluded from source traceability. Native example runs
verify scaffold/toolchain compatibility only. See
[POL-01](Policies/POL-01-Strict-Dotnet-And-Aot.md) for executable local gates.
Hosted/protected CI and real parser acceptance evidence remain unavailable.

The repository bootstrap previously completed its local
`bash scripts/verify.sh` gate on Linux ARM64. That result covered only empty
scaffolds and infrastructure probes; it is historical bootstrap evidence, not
verification of the active parser model or its tests. The standalone
runner/analyzer/TRX probe and scaffold runs do not establish parser coverage
or protected hosted provenance.

## Active requirement methods

| Requirement | Validation method | Evidence or procedure | Owner | Environment / prerequisites | Gap or waiver details |
| --- | --- | --- | --- | --- | --- |
| FEAT-01-001 | portable automated | `CommandModelTests.Create_PreservesOrderedHierarchyMetadataAndCanonicalPaths`; `CommandModelTests.Create_AllowsACommandWithoutChildren` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test methods are declared; run evidence is not yet recorded. |
| FEAT-01-002 | portable automated | `CommandModelTests.Create_RejectsSiblingNameAndAliasCollisions`; `CommandModelTests.Create_AllowsSameNamesOnDisjointBranches`; `CommandModelTests.Create_RejectsEmptyOrMalformedCommandNames`; `CommandModelTests.Create_RejectsCommandCycles`; `CommandModelTests.Create_RejectsCommandNodesWithMultipleParents` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test methods are declared; run evidence is not yet recorded. |
| FEAT-02-001 | portable automated | `CommandModelTests.Create_PreservesOptionAliasesVisibilityAndTypeMetadata` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-02-002 | portable automated | `CommandModelTests.Create_PreservesPositionalDeclarationOrder`; `CommandModelTests.Create_RejectsDuplicatePositionalNames` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test methods are declared; run evidence is not yet recorded. |
| FEAT-02-003 | portable automated | `CommandModelTests.Create_RejectsDuplicateOptionSpellingsButAllowsCaseDifferences` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-02-004 | portable automated | `CommandModelTests.Create_ValidatesArityAndOptionalBarePolicies` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-03-001 | portable automated | `CliParserTests.Parse_PreservesArgumentBoundariesAndEmptyRestValues` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-03-002 | portable automated | `CliParserTests.Parse_ReportsMissingValuesWithoutConsumingTheEndMarker` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-03-004 | portable automated | `CliParserTests.Parse_TreatsDashTokensAsOptionsUntilTheEndMarker` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-04-001 | portable automated | `CliParserTests.Parse_ConvertsBuiltInsAndExplicitEnumMaps` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-04-002 | portable automated | `CliParserTests.Parse_UsesInvariantNumbersByDefaultAndSelectedDecimalSeparator` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-04-003 | portable automated | `CliParserTests.Parse_UsesRegisteredConvertersAndRejectsDuplicateRegistrations` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-04-003 | portable automated | `CliParserTests.Parse_RunsConvertersInInputTokenOrder` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-04-003 | portable automated | `CliParserTests.Parse_PropagatesConverterFaults` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-04-004 | portable automated | `CliParserTests.Parse_ReportsSafeConversionFailuresWithoutPartialValues` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; run evidence is not yet recorded. |
| FEAT-05-001 | portable automated | `CliParserTests.Parse_HandlesBooleanFormsAndExplicitNegation` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-05-002 | portable automated | `CliParserTests.Parse_LeavesFollowingTokensForPositionalBindingAfterBareOptional` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-05-003 | portable automated | `CliParserTests.Parse_AccumulatesCollectionOccurrencesAndRejectsScalarDuplicates` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-05-003 | portable automated | `CliParserTests.Parse_ConsumesGreedyCollectionsUntilTheNextOptionBoundary` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-05-004 | portable automated | `CliParserTests.Parse_BindsRestValuesAndRejectsShortClusters` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-06-001 | portable automated | `CliParserTests.Parse_DistinguishesExplicitAndDefaultValuesAndKeepsRequiredDefaultsInert` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-06-002 | portable automated | `CliParserTests.Parse_InvokesAbsentFactoriesOncePerParseAndNotAfterInvalidInput` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-06-003 | portable automated | `CliParserTests.Parse_RunsValueValidatorsOnAvailableValuesAndSkipsFailedConversions` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-06-003 | portable automated | `CliParserTests.Parse_PropagatesValidatorFaults` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-06-004 | portable automated | `CliParserTests.Parse_RunsCrossValidationOnlyAfterValueValidation` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-06-004 | portable automated | `CliParserTests.Parse_PropagatesCrossValidatorFaults` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-07-001 | portable automated | `CliParserTests.Parse_PreservesProvenanceAndIsolatesResultsAcrossParses` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-07-002 | portable automated | `CommandModelTests.Create_EnforcesConfiguredModelLimits` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-07-002 | portable automated | `CliParserTests.Parse_ReportsStableCategoriesAndCoordinates` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-07-002 | portable automated | `CliParserTests.Parse_ReportsUnsupportedModelsWithoutSelectingACommand` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-07-002 | portable automated | `CliParserTests.Parse_EnforcesConfiguredInputAndDiagnosticLimits` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-07-003 | portable automated | `CliParserTests.Parse_FailsOnUnknownAndExcessInputButExposesItForInspection` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-07-004 | portable automated | `CliParserTests.Parse_SuggestsBoundedVisibleCanonicalNames` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; product test-run evidence is blocked by the missing Governance feed. |
| FEAT-08-001 | portable automated | `HelpDocumentTests.Create_ProjectsVisibleCommandsPositionalsAndOptionAliases` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-08-002 | portable automated | `HelpDocumentTests.Create_SanitizesMetadataAndSuppressesHiddenOrSensitiveDefaults` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-08-003 | portable automated | `HelpDocumentTests.Write_PreservesGroupOrderAndUsesCustomRendererAndSink` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-08-004 | portable automated | `CliParserTests.Parse_HelpBypassesConversionAndRequirednessButNotStructuralErrors` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-08-004 | portable automated | `CommandModelTests.Create_RejectsStandardHelpOptionCollisions` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-09-001 | portable automated | `CliInvokerTests.InvokeAsync_DispatchesOnlyTheSelectedValidatedHandler` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-09-001 | portable automated | `CliInvokerTests.InvokeAsync_NeverDispatchesInvalidOrHelpResultsAndReportsNoHandler` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-09-002 | portable automated | `CliInvokerTests.InvokeAsync_NormalizesSupportedHandlerReturnShapes` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-09-003 | portable automated | `CliInvokerTests.InvokeAsync_HonorsPreCancellationAndStopsBetweenParserCallbacks` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-09-003 | portable automated | `CliInvokerTests.InvokeAsync_ReportsCooperativeCancellationButPreservesCompletedExitCode` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-09-004 | portable automated | `CliInvokerTests.InvokeAsync_PropagatesHandlerFaultsAndIsolatesInvocations` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-01-003 | portable automated | `CliParserTests.Parse_SelectsCanonicalCommandPathsAndHonorsAliasesAndPositionals` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |
| FEAT-03-003 | portable automated | `CliParserTests.Parse_TracksConsumedCommandAndValueTokenPositions` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. Product test-run evidence is blocked by the missing Governance feed. |

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
