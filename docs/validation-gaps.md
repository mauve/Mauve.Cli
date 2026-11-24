# Validation gaps and requirement evidence

This editable ledger starts from a Mauve.Governance template; add a row for
**every active requirement** in `docs/Requirements/`, even when all tests pass.
It is not manifest-owned and updates must not overwrite your evidence. See the
[original Mauve.Governance Policy](https://github.com/mauve/Mauve.Governance/blob/main/src/templates/docs/governance.md)
and the installed [policy](../governance.md).

## Current baseline

The shared `CommandDefinition.MatchesName` refactor passed all 156 Release
runtime tests, including its focused canonical-name/alias test and existing
parser, completion, and multicall regressions. Changed-file formatting and
Governance validation passed. TRX and imported evidence are under
`artifacts/test-results/command-name-helper/`. The full solution and native
gate below precede this refactor and were not rerun for it.

Multicall dispatch passed `bash scripts/verify.sh`: 206 Release tests
(155 runtime, 45 generator, 6 managed AOT), strict build and formatting,
Governance validation, tooling probes, and Linux x64 trimmed/NativeAOT
publication and execution. The log is `artifacts/verification-multicall.log`;
published evidence is under
`artifacts/aot-verification/linux-x64/20261124T074544Z/`.
Both managed and native AOT test runs passed all six tests with imported
requirement metadata, including `VerifyMulticallDispatch` for FEAT-17-004.
The paired multicall examples passed 42 process checks across managed,
trimmed, and NativeAOT outputs: root names, aliases, a separate root,
mapped child and descendant invocation, mapped help, and unmatched-name
failure through real symlinks. These example checks are smoke evidence,
not product MSTest acceptance. Windows normalization branches and other
publication targets remain unexecuted and unverified.

Six focused runtime tests separately passed with metadata-bearing TRX and
imported FEAT-17-001 through FEAT-17-003 evidence under
`artifacts/test-results/multicall/runtime/`. Generated/explicit managed
acceptance also passed separately under
`artifacts/test-results/multicall/aot-managed/`.

After correcting the validation-example smoke expectation to match the
existing public message, the final `bash scripts/verify.sh` gate passed:
197 Release tests, strict build and formatting, tooling probes, Governance
validation and no-op update checks, and trimmed/NativeAOT publication and
execution. The final log is `artifacts/verification-exception-hosting-final.log`;
native evidence is under
`artifacts/aot-verification/linux-x64/20261123T191322Z/`.
The earlier interrupted runs below remain historical observations; the wording
mismatch no longer blocks the gate.

The simplified `examples/16-ExceptionHandling/` pair uses independent
entry points with no shared runner or host-mode dispatch. Both passed strict
Release builds and repository-wide format verification. Twelve managed
example smoke checks covered default/success output, direct exceptions,
mapped exceptions, unknown scenarios and help across both APIs. These are
example smoke checks, not product test acceptance. Earlier publication
observations below apply to the previous, more extensive demo, not these
simplified sources.
The required gate rerun is logged at
`artifacts/verification-simple-exception-examples.log`. It passed the 197-test
solution suite and product NativeAOT acceptance, then stopped at the same
pre-existing validation-example wording mismatch described below. The full
gate was incomplete for that earlier run.

The previous dedicated `examples/16-ExceptionHandling/` declarative/manual pair passed
144 example process checks: 48 each against managed, trimmed and NativeAOT
outputs, with exact exit-code/stdout/stderr parity across both APIs. The
checks cover the README's host modes, direct/mapped failures, renderer
replacement/suppression, builder snapshots, low-level propagation, cancellation,
help and invalid configuration. Published outputs are under
`artifacts/exception-examples/`. These are example smoke observations, not
new product MSTest or imported requirement acceptance evidence.
Both projects passed strict Release builds, locked solution restore and
format verification. The rerun of `bash scripts/verify.sh`, logged at
`artifacts/verification-exception-examples.log`, passed the 197-test solution
suite and managed/NativeAOT product acceptance, then hit the same pre-existing
validation-example wording mismatch described below. The final example-only
low-level outcome clarification was independently rebuilt, format-checked,
republished and checked across all three publication modes; the interrupted
full gate is not claimed as passing.

Exception hosting passed the 197-test Release solution run: 147 runtime,
45 generator and 5 managed AOT tests without skips. The eight focused
exception-hosting runtime tests have metadata-bearing TRX and imported
requirement evidence under `artifacts/test-results/exception-hosting/runtime/`.
All five managed and NativeAOT acceptance tests passed with imported evidence
under `artifacts/aot-verification/linux-x64/20261123T153827Z/`, including
`VerifyExceptionHosting` for FEAT-09-006, FEAT-09-007 and FEAT-09-008.
Both Help-and-Errors examples also returned the expected safe output and
process codes for success, direct exceptions and mapped exceptions.

The full `bash scripts/verify.sh` gate did **not** pass. It completed strict
build, formatting, the solution suite, tooling probes, trimmed command
examples and managed/NativeAOT product acceptance, then stopped in the
unchanged validation-example smoke assertion at
`scripts/verify-conversion-examples.sh:206`. That assertion expects
`String length in UTF-16 code units must be between 2 and 12 (inclusive).`,
while the unchanged `CliValidators.LengthMessage` emits
`String length must be between 2 and 12 (inclusive).`. The mismatch exists in
HEAD and reproduces in the managed validation example as well; no unrelated
validation implementation or smoke expectation was changed. The gate log is
`artifacts/verification-exception-hosting.log`. The final no-op Governance
update check, strict validation and diff check passed independently after
the interrupted gate; this does not turn the incomplete gate into a pass.

Actionable validation reasons passed 139 runtime and 45 generator Release
tests without skips. Metadata-bearing TRX and imported evidence are under
`artifacts/test-results/validation-reasons/`. The added
`Parse_ExplainsCollectionAndCrossFailuresAndBoundsPublicMessages` test covers
collection reasons, untargeted cross-validator reasons, sanitization,
length limits and sensitive/hidden target suppression. Existing item,
typed-exception and generator tests now assert preserved public reasons.
The full `bash scripts/verify.sh` gate passed with 188 Release tests,
including 4 managed AOT tests, formatting and Governance checks.
Trimmed/NativeAOT executions, including validation-example reason assertions,
passed under `artifacts/aot-verification/linux-x64/20261123T121723Z/`;
the gate log is `artifacts/verification-validation-reasons.log`.

Declarative validation passed the 187-test Release solution run: 138 runtime,
45 generator and 4 managed AOT tests. Metadata-bearing runtime and generator
TRX and imported requirement evidence are under
`artifacts/test-results/declarative-validation/`. The full
`bash scripts/verify.sh` gate passed, including Linux x64 trimmed and NativeAOT
publication and execution under
`artifacts/aot-verification/linux-x64/20261123T112339Z/`. The validation
examples passed process checks in both modes; these are smoke checks, not
product test-run acceptance. The script's final summary text was corrected
after that run to account for the two added validation examples.

The converter/union work passed the 159-test Release solution run: 121 runtime
tests, 36 generator tests and two managed AOT tests, without skips. Fresh
metadata-bearing runtime/generator TRX and imported requirement evidence are
under `artifacts/test-results/conversion-unions/`. New source links below
identify the added behavior. The full `bash scripts/verify.sh` gate passed,
including formatting, tooling probes and Linux x64 trimmed/NativeAOT
publication and execution. The gate log is
`artifacts/verification-conversion-unions.log`; fresh native evidence is under
`artifacts/aot-verification/linux-x64/20261122T170808Z/`. Managed and native
focused TRX each contain two passing tests with matching Governance metadata.
The four new conversion examples passed 56 process checks across eight distinct
trimmed/native publications under that directory's `conversion-examples/`.
These process checks are environment-gated smoke evidence, not product MSTest
or Governance test-run acceptance.

The hierarchy and help-tree change passed a 143-test Release MTP run, including
34 generator tests, 107 runtime tests and 2 managed AOT tests. Per-project
metadata-bearing reports and imported requirement evidence are under
`artifacts/test-results/hierarchy-help-tree/`; the independently executed
trimmed and NativeAOT checks are under
`artifacts/aot-verification/linux-x64/20261122T154453Z/`. After configuring
the declarative example analyzer exception, the full `scripts/verify.sh` gate
passed, including 143 Release tests, formatting, tooling probes and Linux x64
trimmed/NativeAOT checks under
`artifacts/aot-verification/linux-x64/20261122T160730Z/`.

FEAT-01-001 through FEAT-01-003, FEAT-02-001 through FEAT-02-004,
FEAT-03-001 through FEAT-03-004, FEAT-04-001 through FEAT-04-004, and all of
FEAT-05, FEAT-06, FEAT-07, FEAT-08, FEAT-09, FEAT-10, FEAT-11, FEAT-12,
FEAT-13 through FEAT-15 are active except retired FEAT-15-007.
Portable test methods are declared for each. The XML-documentation change
passed the repository-wide Release MTP run with 132 tests:
102 runtime tests, 28 generator tests and 2 focused AOT tests. Per-project
metadata-bearing TRX and imported requirement evidence are under
`artifacts/test-results/xml-documentation-20261122-final/`.

The earlier `bash scripts/verify.sh` gate and full-solution format verification
were blocked by the removal of the CA1822 suppression from
`examples/01-Hello/Declarative/Program.cs`: its instance handler does not access
instance data. All changed projects built
with strict analyzers, and changed-project format verification, locked restore,
Governance validation/source generation, tooling probes, and the no-op managed
update check passed independently. The later full gate passed after the
declarative example analyzer exception was configured.

The first post-publication `--no-build` solution test attempt could not launch
the managed AOT test apphost after native publication. Rebuilding that test
project restored the managed host; the final 132-test run passed without skips.
The initial 130-test artifacts remain separate under
`artifacts/test-results/xml-documentation-20261122/` and are not claimed as
a complete suite observation.

`bash scripts/verify-aot.sh` passed independently on Linux x64, including
XML-derived descriptions and shell example filtering/expansion in the generated
AOT scenario. Evidence is under
`artifacts/aot-verification/linux-x64/20261122T133238Z/`.
Both new XML-documentation examples also passed managed trimmed and NativeAOT
publication/run checks for help metadata, attribute precedence, C# example
exclusion, and actual command invocation; see `XmlDocumentation-examples.log`
and `XmlDocumentation-environment.txt` in that directory.

The standalone runner/analyzer/MTP TRX probe verifies only infrastructure.
Its synthetic feature is intentionally outside this repository's product
inventory and is excluded from source traceability. The focused FEAT-13 and FEAT-15 tests passed through managed MTP and as a
NativeAOT executable with exact test-count and metadata parity. Both actual
previous Todo and FileTool consumers and the replacement Commands examples
passed managed trimmed and NativeAOT publish/run checks on Linux x64; see
FEAT-13 and the rows below. See
[POL-01](Policies/POL-01-Strict-Dotnet-And-Aot.md) for executable local gates.
The 2026-11-22 OptionPack redesign passed `bash scripts/verify.sh` on Linux
x64, including its trimmed and NativeAOT checks; evidence is under
`artifacts/aot-verification/linux-x64/20261122T125059Z/`.
Hosted/protected CI remains unavailable.

The repository bootstrap previously completed its local
`bash scripts/verify.sh` gate on Linux ARM64. That result covered only empty
scaffolds and infrastructure probes; it is historical bootstrap evidence, not
verification of the active parser model or its tests. No protected hosted run
is claimed.

## Active requirement methods

| Requirement | Validation method | Evidence or procedure | Owner | Environment / prerequisites | Gap or waiver details |
| --- | --- | --- | --- | --- | --- |
| FEAT-01-003 | portable automated | `CommandModelTests.MatchesName_UsesOrdinalCanonicalNamesAndAliases` | Mauve.Cli maintainers | .NET 11 | Passed in the 156-test Release runtime suite; metadata-bearing TRX and imported evidence under `artifacts/test-results/command-name-helper/`. |
| FEAT-01-001 | portable automated | `CommandModelTests.GetPath_ReturnsModelDefinitionsAndRejectsForeignCommands` | Mauve.Cli maintainers | .NET 11 | Passed in the focused 23-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-01-003 | portable automated | `CommandModelTests.TryGetChild_FindsDirectChildrenByOrdinalNameOrAlias` | Mauve.Cli maintainers | .NET 11 | Passed in the focused 23-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-03-002 | portable automated | `CommandModelTests.FormatOptionNames_UsesConfiguredPrefixesWithoutNormalizing` | Mauve.Cli maintainers | .NET 11 | Passed in the focused 23-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-07-001 | portable automated | `CliParserTests.ParsedValue_TryGetValueDistinguishesUnavailableNullAndIncompatibleValues` | Mauve.Cli maintainers | .NET 11 | Passed in a focused 38-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-01-001 | portable automated | `CommandModelTests.Contains_UsesSnapshotIdentityForRootAndDescendants` | Mauve.Cli maintainers | .NET 11 | Passed in a focused 90-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-15-001 | portable automated | `CommandModelTests.TryGetDirective_UsesOrdinalNamesIncludingHiddenDirectives` | Mauve.Cli maintainers | .NET 11 | Passed in a focused 90-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-09-006 | portable automated | `ExceptionHostingTests.ExceptionMappings_TryResolveSelectsExactAndMostSpecificAssignableTypeRegardlessOfOrder`; `ExceptionHostingTests.ExceptionMappings_TryResolveReportsUnmatchedAndPropagatesNullAndMapperFaults` | Mauve.Cli maintainers | .NET 11 | Passed in a focused Release MTP run; metadata-bearing TRX and imported evidence under `artifacts/test-results/exception-resolution/`. |
| FEAT-10-005 | portable automated | `CompletionCommandLineTests.ParseCommandLine_UsesUtf16OffsetsAndModelLimits` | Mauve.Cli maintainers | .NET 11 | Passed in a focused 13-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-17-001 | portable automated | `MulticallDispatchTests.Route_SnapshotsPathsAndRejectsMalformedConfiguration`; `MulticallDispatchTests.Dispatch_RejectsInvalidOrConflictingRoutesBeforeCallbacks` | Mauve.Cli maintainers | .NET 11 | Passed: two tests; TRX and imported evidence under `artifacts/test-results/multicall/runtime/`. |
| FEAT-17-002 | portable automated | `MulticallDispatchTests.Dispatch_SelectsImplicitRootsAndPlatformNormalizedExecutableNames`; `MulticallDispatchTests.Dispatch_RejectsInvalidOrConflictingRoutesBeforeCallbacks` | Mauve.Cli maintainers | .NET 11; platform-specific assertions | Passed: two tests on Linux x64; TRX and imported evidence under `artifacts/test-results/multicall/runtime/`. Windows branches remain unexecuted. |
| FEAT-17-003 | portable automated | `MulticallDispatchTests.Dispatch_PreservesCanonicalAncestryArgumentsDirectivesAndOptionScope`; `MulticallDispatchTests.Dispatch_RendersMappedHelpWithoutRepeatingItsCommandPrefix`; `MulticallDispatchTests.Dispatch_PreservesCancellationExceptionsAndInvalidInputCoordinates` | Mauve.Cli maintainers | .NET 11 | Passed: three tests; TRX and imported evidence under `artifacts/test-results/multicall/runtime/`. |
| FEAT-17-004 | portable automated | `AotAcceptanceTests.VerifyMulticallDispatch` | Mauve.Cli maintainers | .NET 11 | Passed: generated/explicit managed acceptance; TRX and imported evidence under `artifacts/test-results/multicall/aot-managed/`. |
| FEAT-17-004 | environment-gated automated | `AotAcceptanceTests.VerifyMulticallDispatch`; `scripts/verify-multicall-examples.sh` via `scripts/verify-aot.sh` | Mauve.Cli maintainers | Linux x64; native compiler, linker and runtime packs | Passed: managed/native product TRX with exact six-test metadata parity and 42 real-symlink example process checks under `artifacts/aot-verification/linux-x64/20261124T074544Z/`. Example smoke checks are not MSTest evidence; other RIDs remain unverified. |
| FEAT-06-005 | portable automated | `CliParserTests.Parse_BuiltInRulesAndCustomItemRejectionsUseValueCoordinates`; `CliParserTests.Parse_ExplainsCollectionAndCrossFailuresAndBoundsPublicMessages` | Mauve.Cli maintainers | .NET 11 | Passed: two tests; TRX and imported evidence under `artifacts/test-results/validation-reasons/`. |
| FEAT-06-006 | portable automated | `CliParserTests.Parse_BuiltInRulesAndCustomItemRejectionsUseValueCoordinates`; `CliParserTests.Parse_CrossValidationTargetsAndContractFaultsAreDistinguished`; `CliParserTests.Parse_ExplainsCollectionAndCrossFailuresAndBoundsPublicMessages`; `CliInvokerTests.InvokeAsync_ValidationRejectionsDoNotInventEnvironmentOrDefaultCoordinates` | Mauve.Cli maintainers | .NET 11 | Passed: four tests; TRX and imported evidence under `artifacts/test-results/validation-reasons/`. |
| FEAT-11-008 | portable automated | `CliModelGeneratorTests.Generate_RegistersValidationRulesAndCrossValueTargets`; `CliModelGeneratorTests.Generate_RejectsInvalidValidationDeclarations` | Mauve.Cli maintainers | .NET 11; Roslyn | Passed: two tests; TRX and imported evidence under `artifacts/test-results/validation-reasons/`. |
| FEAT-04-001 | portable automated | `BuiltInConversionTests.Parse_ConvertsNewScalarTypes`; `BuiltInConversionTests.Parse_PathsAreLexicalAndNeedNotExist`; `BuiltInConversionTests.Parse_CreatesTypedCollectionsAndNullableValues` | Mauve.Cli maintainers | .NET 11 | Passed in the 121-test Release runtime run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-04-002 | portable automated | `BuiltInConversionTests.Parse_HalfUsesSelectedNumericCultureOnly` | Mauve.Cli maintainers | .NET 11 | Passed in the 121-test Release runtime run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-04-003 | portable automated | `ConverterRegistrationTests.Parse_UsesDefinitionModelAndHostConvertersWithoutMutatingSnapshots`; `ConverterRegistrationTests.Run_UsesHostConfigurationAndRejectsConflictingRegistriesBeforeCallbacks`; `ConverterRegistrationTests.Create_RejectsMismatchedConvertersAndChildRegistrations`; `ConverterRegistrationTests.Parse_UnionCasesUseFirstSuccessAndSupportNestedNullableCollections`; `ConverterRegistrationTests.Parse_UnionFaultsPropagateAndInvalidDependenciesAreRejected` | Mauve.Cli maintainers | .NET 11 | Passed in the 121-test Release runtime run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-04-004 | portable automated | `BuiltInConversionTests.Parse_RejectsMalformedNewTypesWithSafeDiagnostics`; `BuiltInConversionTests.Parse_InvalidCollectionElementDoesNotCommitPartialValues`; `ConverterRegistrationTests.Parse_UnionCasesUseFirstSuccessAndSupportNestedNullableCollections` | Mauve.Cli maintainers | .NET 11 | Passed in the 121-test Release runtime run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-10-003 | portable automated | `ConverterRegistrationTests.Complete_UsesOnlyApprovedDefinitionConverters` | Mauve.Cli maintainers | .NET 11 | Passed with definition-local completion-safety precedence in the 121-test runtime run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-10-001 | portable automated | `ConverterRegistrationTests.Complete_DoesNotUseEnumNamesFromAnOverriddenConverter` | Mauve.Cli maintainers | .NET 11 | Passed with local and exact nullable override assertions in the 121-test runtime run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-11-003 | portable automated | `CliModelGeneratorTests.Generate_BindsTypeAndPropertyConvertersEnumsAndOrderedUnions` | Mauve.Cli maintainers | .NET 11; Roslyn 5.12.0 | Generated scalar/union/nullable-collection/directive, nullable-reference and high-level-host assertions passed in the 36-test generator run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-11-004 | portable automated | `CliModelGeneratorTests.Generate_RejectsMismatchedProvidersAndCustomUnionDiscovery` | Mauve.Cli maintainers | .NET 11; Roslyn 5.12.0 | Passed in the 36-test Release generator run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-15-004 | portable automated | `ConverterRegistrationTests.Invoke_UsesDefinitionConvertersForDirectiveValues` | Mauve.Cli maintainers | .NET 11 | Passed in the 121-test Release runtime run; metadata-bearing TRX imported under `artifacts/test-results/conversion-unions/`. |
| FEAT-04-001 | environment-gated automated | `scripts/verify-conversion-examples.sh` generated and explicit built-in examples | Mauve.Cli maintainers | Linux x64; native compiler, linker and runtime packs | Distinct trimmed and NativeAOT publications passed scalar, nullable/collection/union and invalid-input process checks under `artifacts/aot-verification/linux-x64/20261122T170808Z/conversion-examples/`; not MSTest/TRX acceptance. |
| FEAT-04-003 | environment-gated automated | `scripts/verify-conversion-examples.sh` generated and explicit custom converter examples | Mauve.Cli maintainers | Linux x64; native compiler, linker and runtime packs | Distinct trimmed and NativeAOT publications passed type/property/directive conversion, local override and invalid-input process checks under `artifacts/aot-verification/linux-x64/20261122T170808Z/conversion-examples/`; not MSTest/TRX acceptance. |
| FEAT-13-002 | environment-gated automated | `scripts/verify-aot.sh` and `AotConversionScenario` | Mauve.Cli maintainers | Linux x64; native compiler, linker and runtime packs | Generated/explicit model-owned converters, ordered unions and nullable custom collections passed in both managed and native focused tests; metadata-bearing TRX imported under `artifacts/aot-verification/linux-x64/20261122T170808Z/`. The four new examples separately passed trimmed/native process checks. |
| FEAT-13-003 | environment-gated automated | `scripts/verify-aot.sh` and `AotConversionScenario` | Mauve.Cli maintainers | Linux x64; native compiler, linker and runtime packs | Managed/native focused tests passed with exact two-test and requirement metadata parity under `artifacts/aot-verification/linux-x64/20261123T092925Z/`, including generated initializer-default and required-attribute behavior. Trimmed/native examples passed their process checks; other RIDs remain unverified. |
| FEAT-08-006 | portable automated | `HelpTreeTests.Render_ProjectsVisibleFullAndSelectedTreesSafely`; `HelpTreeTests.Write_UsesSanitizedProjectionAndPropagatesOutputFaults`; `CommandModelTests.Create_InstallsHelpTreeAndAccountsForItsLimitsAndCollisions`; `CliParserTests.Parse_HelpTreeHonorsHelpPrecedenceAndConflicts`; `CliInvokerTests.Run_HelpTreeUsesHostOutputAndPreservesDirectiveOrder` | Mauve.Cli maintainers | .NET 11 | All five methods passed in the 107-test Release runtime MTP run; metadata-bearing TRX and imported evidence are under `artifacts/test-results/hierarchy-help-tree/`. |
| FEAT-08-006 | environment-gated automated | `scripts/verify-aot.sh` root and selected help-tree checks for generated and explicit examples | Mauve.Cli maintainers | Linux x64; native compiler, linker and runtime packs | All four trimmed and NativeAOT publications executed both tree modes successfully. Logs are under `artifacts/aot-verification/linux-x64/20261122T154453Z/`. |
| FEAT-11-001 | portable automated | `CliModelGeneratorTests.Generate_CombinesParentAndChildEdgesInGlobalSourceOrder`; `CliModelGeneratorTests.Generate_UpdatesParentOnlyHierarchyWhenAnEdgeChanges` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Both methods passed in the 34-test Release generator MTP run; metadata-bearing TRX and imported evidence are under `artifacts/test-results/hierarchy-help-tree/`. |
| FEAT-11-002 | portable automated | `CliModelGeneratorTests.Generate_CombinesParentAndChildEdgesInGlobalSourceOrder`; `CliModelGeneratorTests.Generate_UpdatesParentOnlyHierarchyWhenAnEdgeChanges` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Both methods passed in the 34-test Release generator MTP run; metadata-bearing TRX and imported evidence are under `artifacts/test-results/hierarchy-help-tree/`. |
| FEAT-11-004 | portable automated | `CliModelGeneratorTests.Generate_ReportsBothDeclarationsOfTheSameEdge`; `CliModelGeneratorTests.Generate_ConflictingEdgeDoesNotInvalidateAnUnrelatedRoot`; `CliModelGeneratorTests.Generate_RejectsCyclesAcrossAllDeclaredCommands`; `CliModelGeneratorTests.Generate_RejectsInvalidParentTargetsAndRootChildren` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | All four methods passed in the 34-test Release generator MTP run; metadata-bearing TRX and imported evidence are under `artifacts/test-results/hierarchy-help-tree/`. |
| FEAT-08-005 | portable automated | `HelpDocumentTests.Create_ExpandsCanonicalExamplesWithoutReplacingDeclaredCommandNames`; `HelpDocumentTests.Create_SnapshotsExamplesAndRejectsInvalidExampleInputs`; `CliInvokerTests.Run_PassesExecutableExamplesToDefaultHelpRenderer`; `CliInvokerTests.RunAsync_PassesExecutableExamplesToCustomHelpRenderer` | Mauve.Cli maintainers | .NET 11 | All four methods passed in the 102-test runtime suite and final 132-test Release MTP run; metadata-bearing TRX imported under `artifacts/test-results/xml-documentation-20261122-final/`. The subsequent full repository gate passed after the Hello CA1822 configuration change. |
| FEAT-08-005 | portable automated | `HelpDocumentTests.Create_AbbreviatesLongExecutableOnlyInExampleSubstitutions` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 149-test Release product MTP run for the help presentation changes. |
| FEAT-11-006 | portable automated | `CliModelGeneratorTests.Generate_InfersXmlMetadataAndShellExamplesWithExplicitModelParity`; `CliModelGeneratorTests.Generate_PreservesExplicitXmlMetadataOverridesIncludingEmptyAndNull`; `CliModelGeneratorTests.Generate_RejectsInvalidShellExamplesButIgnoresOtherLanguages`; `CliModelGeneratorTests.Generate_UpdatesXmlOnlyEditsWithStableUnchangedOutput` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | All four methods passed in the 28-test generator suite and final 132-test Release MTP run, including malformed XML diagnostics; metadata-bearing TRX imported under `artifacts/test-results/xml-documentation-20261122-final/`. The subsequent full repository gate passed after the Hello CA1822 configuration change. |
| FEAT-11-007 | portable automated | `CliModelGeneratorTests.Generate_InfersRequiredAndConstantDefaultsForCommandsAndPacks`; `CliModelGeneratorTests.Generate_RequiresPositionalWithRequiredModifier`; `CliModelGeneratorTests.Generate_UsesTypedInvariantConstantDisplaysAndNullableDefaults`; `CliModelGeneratorTests.Generate_RejectsUnsupportedInitializersAndUnsafeRequiredMembers`; `CliModelGeneratorTests.Generate_UpdatesInitializerAndRequiredAttributeEdits` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | All five methods passed in the 42-test Release generator MTP run; metadata-bearing TRX and imported evidence are under `artifacts/test-results/declarative-inference-20261123-final/`. |
| FEAT-16-001 | portable automated | `CliInvokerTests.InvokeAsync_UsesOptionBindingOverridesAndSkipsEnvironmentForHelpOrInvalidOrExplicitOptions`; `CliInvokerTests.InvokeAsync_UsesOptionEligibilityDefaultAndRejectsEnvironmentNameCollisions`; `HelpDocumentTests.Run_HelpPrintsEnvironmentNamesInlineOnlyWhenEnabled` | Mauve.Cli maintainers | .NET 11; injected environment lookup | All three methods passed in the 133-test Release runtime MTP run; they cover explicit/generated names, eligibility and collisions, inline help names only when host processing is enabled, host prefixes, help/structurally invalid argv bypass and no environment lookup during help. |
| FEAT-16-002 | portable automated | `CliInvokerTests.InvokeAsync_BindsEnvironmentValuesBetweenDefaultsAndArgv` | Mauve.Cli maintainers | .NET 11; injected environment lookup | Passed in the 132-test Release runtime MTP run; covers declared default, environment and argv precedence, required environment input, distinct provenance, no argv coordinates, and skipped lookup for explicit argv. |
| FEAT-16-003 | portable automated | `CliInvokerTests.InvokeAsync_ConvertsEnvironmentFlagsCountsAndCsvCollectionsSafely`; `CliInvokerTests.InvokeAsync_EnforcesEnvironmentValueAndCollectionLimits`; `CliInvokerTests.InvokeAsync_PropagatesEnvironmentLookupFaults` | Mauve.Cli maintainers | .NET 11; injected environment lookup | All three methods passed in the 132-test Release runtime MTP run; covers boolean/counting conversion, bounded custom-delimiter CSV parsing, malformed values, safe diagnostics, limit enforcement and lookup-fault propagation. |
| FEAT-16-004 | portable automated | `CliModelGeneratorTests.Generate_LowersEnvironmentMetadataForCommandsAndOptionPacks` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Passed in the 43-test Release generator MTP run; generated command and OptionPack metadata bind identically through the invocation path. |
| FEAT-16-004 | environment-gated automated | `AotAcceptanceTests.VerifyGeneratedAndExplicitEnvironmentBinding`; `scripts/verify-aot.sh` | Mauve.Cli maintainers | Linux x64; native compiler, linker and runtime packs | Managed and NativeAOT acceptance each passed all three tests. Governance evidence reports FEAT-16 and FEAT-16-004 passed; artifacts are under `artifacts/aot-verification/linux-x64/20261123T100805Z/`. |
| FEAT-01-001 | portable automated | `CommandModelTests.Create_PreservesOrderedHierarchyMetadataAndCanonicalPaths`; `CommandModelTests.Create_AllowsACommandWithoutChildren` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test methods are declared; passed in the strict 83-test MTP suite. |
| FEAT-01-002 | portable automated | `CommandModelTests.Create_RejectsSiblingNameAndAliasCollisions`; `CommandModelTests.Create_AllowsSameNamesOnDisjointBranches`; `CommandModelTests.Create_RejectsEmptyOrMalformedCommandNames`; `CommandModelTests.Create_RejectsCommandCycles`; `CommandModelTests.Create_RejectsCommandNodesWithMultipleParents` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test methods are declared; passed in the strict 83-test MTP suite. |
| FEAT-02-001 | portable automated | `CommandModelTests.Create_PreservesOptionAliasesVisibilityAndTypeMetadata`; `CommandModelTests.Create_PreservesOmittedAndExplicitEmptyDisplayMetadata` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test methods are declared; passed in the strict 83-test MTP suite. |
| FEAT-02-002 | portable automated | `CommandModelTests.Create_PreservesPositionalDeclarationOrder`; `CommandModelTests.Create_RejectsDuplicatePositionalNames`; `CommandModelTests.Create_PreservesOmittedAndExplicitEmptyDisplayMetadata` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test methods are declared; passed in the strict 83-test MTP suite. |
| FEAT-02-003 | portable automated | `CommandModelTests.Create_RejectsDuplicateOptionSpellingsButAllowsCaseDifferences` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-02-004 | portable automated | `CommandModelTests.Create_ValidatesArityAndOptionalBarePolicies` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-03-001 | portable automated | `CliParserTests.Parse_PreservesArgumentBoundariesAndEmptyRestValues` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-03-002 | portable automated | `CliParserTests.Parse_ReportsMissingValuesWithoutConsumingTheEndMarker` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-03-004 | portable automated | `CliParserTests.Parse_TreatsDashTokensAsOptionsUntilTheEndMarker` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-04-001 | portable automated | `CliParserTests.Parse_ConvertsBuiltInsAndExplicitEnumMaps` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-04-002 | portable automated | `CliParserTests.Parse_UsesInvariantNumbersByDefaultAndSelectedDecimalSeparator` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-04-003 | portable automated | `CliParserTests.Parse_UsesRegisteredConvertersAndRejectsDuplicateRegistrations` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-04-003 | portable automated | `CliParserTests.Parse_RunsConvertersInInputTokenOrder` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-04-003 | portable automated | `CliParserTests.Parse_PropagatesConverterFaults` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-04-004 | portable automated | `CliParserTests.Parse_ReportsSafeConversionFailuresWithoutPartialValues` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; passed in the strict 83-test MTP suite. |
| FEAT-05-001 | portable automated | `CliParserTests.Parse_HandlesBooleanFormsAndExplicitNegation` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-05-002 | portable automated | `CliParserTests.Parse_LeavesFollowingTokensForPositionalBindingAfterBareOptional` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-05-003 | portable automated | `CliParserTests.Parse_AccumulatesCollectionOccurrencesAndRejectsScalarDuplicates` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-05-003 | portable automated | `CliParserTests.Parse_ConsumesGreedyCollectionsUntilTheNextOptionBoundary` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-05-004 | portable automated | `CliParserTests.Parse_BindsRestValuesAndRejectsUnknownShortClusters`; `CliParserTests.Parse_ExpandsZeroValueShortClustersAndCountsExplicitCountingOptions`; `CliParserTests.Parse_RejectsInvalidClustersWithoutApplyingTheirValidPrefix` | Mauve.Cli maintainers | .NET 11; no privileged environment | Methods are declared. Current local MTP runs passed, but no metadata-bearing TRX has yet been imported as Governance evidence for the changed behavior. |
| FEAT-03-002 | portable automated | `CliParserTests.Parse_UsesConfiguredPrefixesAndKeepsTheEndMarkerFixed`; `CompletionServiceTests.CompleteAsync_UsesConfiguredPrefixesAndShortNames` | Mauve.Cli maintainers | .NET 11; no privileged environment | Methods are declared. Current local MTP runs passed, but no metadata-bearing TRX has yet been imported as Governance evidence for the changed behavior. |
| FEAT-11-001 | portable automated | `CliModelGeneratorTests.Generate_SupportsShortOnlyCountingOptions` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Method is declared and passed in a focused local MTP run; no metadata-bearing TRX has yet been imported as Governance evidence for the changed declaration surface. |
| FEAT-06-001 | portable automated | `CliParserTests.Parse_DistinguishesExplicitAndDefaultValuesAndKeepsRequiredDefaultsInert` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-06-002 | portable automated | `CliParserTests.Parse_InvokesAbsentFactoriesOncePerParseAndNotAfterInvalidInput` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-06-003 | portable automated | `CliParserTests.Parse_RunsValueValidatorsOnAvailableValuesAndSkipsFailedConversions` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-06-003 | portable automated | `CliParserTests.Parse_PropagatesValidatorFaults` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-06-004 | portable automated | `CliParserTests.Parse_RunsCrossValidationOnlyAfterValueValidation` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-06-004 | portable automated | `CliParserTests.Parse_PropagatesCrossValidatorFaults` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-07-001 | portable automated | `CliParserTests.Parse_PreservesProvenanceAndIsolatesResultsAcrossParses` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-07-002 | portable automated | `CommandModelTests.Create_EnforcesConfiguredModelLimits` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-07-002 | portable automated | `CliParserTests.Parse_ReportsStableCategoriesAndCoordinates` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-07-002 | portable automated | `CliParserTests.Parse_NamesMissingVisibleArgumentsWithoutInventingPositions` | Mauve.Cli maintainers | .NET 11; no privileged environment | Missing positional names and hidden-name redaction passed in the strict 87-test MTP suite. |
| FEAT-07-002 | portable automated | `CliParserTests.Parse_ReportsUnsupportedModelsWithoutSelectingACommand` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-07-002 | portable automated | `CliParserTests.Parse_EnforcesConfiguredInputAndDiagnosticLimits` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-07-003 | portable automated | `CliParserTests.Parse_FailsOnUnknownAndExcessInputButExposesItForInspection` | Mauve.Cli maintainers | .NET 11; no privileged environment | Bounded, escaped positional text and its suppression passed in the strict 87-test MTP suite. |
| FEAT-07-004 | portable automated | `CliParserTests.Parse_SuggestsBoundedVisibleCanonicalNames` | Mauve.Cli maintainers | .NET 11; no privileged environment | Test method is declared; also passed in the strict 83-test MTP suite. |
| FEAT-08-001 | portable automated | `HelpDocumentTests.Create_ProjectsVisibleCommandsPositionalsAndOptionAliases` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-08-002 | portable automated | `HelpDocumentTests.Create_SanitizesMetadataAndSuppressesHiddenOrSensitiveDefaults` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-08-003 | portable automated | `HelpDocumentTests.Write_PreservesGroupOrderAndUsesCustomRendererAndSink` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-08-003 | portable automated | `HelpDocumentTests.Render_SeparatesAndIndentsOptionsAndPlacesDirectivesLast` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 149-test Release product MTP run for the help presentation changes. |
| FEAT-08-004 | portable automated | `CliParserTests.Parse_HelpBypassesConversionAndRequirednessButNotStructuralErrors` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-08-004 | portable automated | `CommandModelTests.Create_RejectsStandardHelpOptionCollisions` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-09-001 | portable automated | `CliInvokerTests.InvokeAsync_DispatchesOnlyTheSelectedValidatedHandler` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-09-001 | portable automated | `CliInvokerTests.InvokeAsync_NeverDispatchesInvalidOrHelpResultsAndReportsNoHandler` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-09-002 | portable automated | `CliInvokerTests.InvokeAsync_NormalizesSupportedHandlerReturnShapes` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-09-003 | portable automated | `CliInvokerTests.InvokeAsync_HonorsPreCancellationAndStopsBetweenParserCallbacks` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-09-003 | portable automated | `CliInvokerTests.InvokeAsync_ReportsCooperativeCancellationButPreservesCompletedExitCode` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-09-004 | portable automated | `CliInvokerTests.InvokeAsync_PropagatesHandlerFaultsAndIsolatesInvocations` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-09-006 | portable automated | `ExceptionHostingTests.Run_HandlesDirectExceptionsAcrossGeneratedAndExplicitHosts`; `ExceptionHostingTests.Run_ChoosesMostSpecificMappingAndExplicitOverrides`; `ExceptionHostingTests.InvokeAsync_PreservesFaultsAndCancellationDespiteHostMappings`; `ExceptionHostingTests.Run_MapsInvocationCallbacksButDoesNotRemapMappingFaults`; `ExceptionHostingTests.Run_MapsAdditionalDirectivesWithoutDispatchingCommandsOrCatchingSetupFaults`; `ExceptionHostingTests.Builder_RejectsInvalidMappingsAndKeepsCompositionAtomic` | Mauve.Cli maintainers | .NET 11 | Passed in the 197-test Release suite and focused metadata-bearing runtime TRX; imported evidence under `artifacts/test-results/exception-hosting/runtime/`. Full-gate limitation described above. |
| FEAT-09-007 | portable automated | `ExceptionHostingTests.Run_HandlesDirectExceptionsAcrossGeneratedAndExplicitHosts`; `ExceptionHostingTests.Run_UsesRenderingContextAndRejectsBrokenRenderersWithoutRemapping` | Mauve.Cli maintainers | .NET 11 | Passed in the 197-test Release suite and focused metadata-bearing runtime TRX; imported evidence under `artifacts/test-results/exception-hosting/runtime/`. |
| FEAT-09-008 | portable automated | `ExceptionHostingTests.Builder_RejectsInvalidMappingsAndKeepsCompositionAtomic`; `ExceptionHostingTests.Builder_PreservesSnapshotsAndConstructorBehavior` | Mauve.Cli maintainers | .NET 11 | Passed in the 197-test Release suite and focused metadata-bearing runtime TRX; imported evidence under `artifacts/test-results/exception-hosting/runtime/`. |
| FEAT-09-006 | environment-gated automated | `AotAcceptanceTests.VerifyExceptionHosting` | Mauve.Cli maintainers | Linux x64, .NET 11, native compiler/linker; `bash scripts/verify-aot.sh` | Managed and NativeAOT execution passed with imported metadata-bearing evidence under `artifacts/aot-verification/linux-x64/20261123T153827Z/`. Later unrelated validation-example smoke failed; the complete script is not claimed as passing. |
| FEAT-09-007 | environment-gated automated | `AotAcceptanceTests.VerifyExceptionHosting` | Mauve.Cli maintainers | Linux x64, .NET 11, native compiler/linker; `bash scripts/verify-aot.sh` | Managed and NativeAOT execution passed with imported metadata-bearing evidence under `artifacts/aot-verification/linux-x64/20261123T153827Z/`. |
| FEAT-09-008 | environment-gated automated | `AotAcceptanceTests.VerifyExceptionHosting` | Mauve.Cli maintainers | Linux x64, .NET 11, native compiler/linker; `bash scripts/verify-aot.sh` | Managed and NativeAOT execution passed with imported metadata-bearing evidence under `artifacts/aot-verification/linux-x64/20261123T153827Z/`. |
| FEAT-09-005 | portable automated | `CliInvokerTests.Run_UsesConfiguredHostOutputAndExitCodes`; `CliInvokerTests.RunAsync_MapsPreCancellationToConfiguredExitCode`; published Todo CLI process-argument invocation in `scripts/verify-aot.sh` | Mauve.Cli maintainers | .NET 11; Linux x64 for trimmed/NativeAOT example acceptance | Both product tests passed in the 90-test strict MTP suite. The Todo process-argument invocation passed for trimmed and NativeAOT publications on Linux x64 in `bash scripts/verify.sh`. |
| FEAT-09-005 | portable automated | `CliInvokerTests.RunAsync_RegistersDirectivesAndUsesInvocationWriters`; `CliInvokerTests.Run_RejectsAsynchronousOrDuplicateAdditionalDirectives` | Mauve.Cli maintainers | .NET 11 | Passed in the 111-test strict MTP suite and full repository gate. |
| FEAT-01-003 | portable automated | `CliParserTests.Parse_SelectsCanonicalCommandPathsAndHonorsAliasesAndPositionals` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-03-003 | portable automated | `CliParserTests.Parse_TracksConsumedCommandAndValueTokenPositions` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-12-001 | portable automated | `CliParserTests.Parse_RecognizesInheritedOptionsOnlyInSelectedScope` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-12-002 | portable automated | `CliParserTests.Parse_BindsInheritedOptionsBeforeBetweenAndAfterSelectedPath` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-12-002 | portable automated | `CliModelGeneratorTests.Generate_ExposesRootPackInheritedOptionBeforeAndAfterChild` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | An inherited option declared in a generated root pack was bound before and after the child command in the focused Debug run and strict 124-test Release suite. |
| FEAT-12-003 | portable automated | `CommandModelTests.Create_RejectsInheritedOptionCollisionsButAllowsDisjointScopes` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-12-004 | portable automated | `HelpDocumentTests.Create_ProjectsInheritedOptionsWithTheirDeclaringOwner` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-12-004 | portable automated | `CliInvokerTests.InvokeAsync_ExposesInheritedValuesFromTheirDeclaringCommand` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-11-001 | portable automated | `CliModelGeneratorTests.Generate_CompilesAndRunsNestedAliasBinding`; `CliModelGeneratorTests.Generate_InfersNamesSourceOrderAndCardinality` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Generic attributes and inferred names/cardinality passed in the strict 86-test MTP suite. |
| FEAT-11-001 | portable automated | `CliModelGeneratorTests.Generate_CompilesAndAwaitsAllSupportedAsyncHandlerShapes` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | All four supported async return shapes and supported parameter forms passed in the focused generator run and strict 86-test MTP suite. |
| FEAT-11-001 | portable automated | `CliModelGeneratorTests.Generate_ForwardCancellationTokenToAsyncHandler` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Generated-handler cancellation passed in the focused generator run and strict 86-test MTP suite. |
| FEAT-11-001 | portable automated | `CliModelGeneratorTests.Generate_OrdersInjectedPacksAndIgnoresParameterNames` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Constructor pack order, inferred Boolean flags and parameter-independent identity passed in the focused Debug run and strict 124-test Release suite. |
| FEAT-11-002 | portable automated | `CliModelGeneratorTests.Generate_UpdatesAliasesAndProducesStableUnchangedOutput`; `CliModelGeneratorTests.Generate_InfersNamesSourceOrderAndCardinality`; `CliModelGeneratorTests.Generate_OrdersPartialMembersByCompilationSyntaxTreeThenSpan` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Incremental stability and source-order inference passed in the strict 86-test MTP suite. |
| FEAT-11-003 | portable automated | `CliModelGeneratorTests.Generate_CompilesAndRunsNestedAliasBinding` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Package-free parity harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 86-test MTP suite. |
| FEAT-11-004 | portable automated | `CliModelGeneratorTests.Generate_ReportsUnsupportedMembersAndEmitsNoInvalidRoot`; `CliModelGeneratorTests.Generate_ReportsDuplicateSiblingAliasesAndEmitsNoRoot`; `CliModelGeneratorTests.Generate_ValidatesExplicitInferenceOverrides` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Unsupported shapes, collisions and incompatible overrides are rejected in the strict 86-test MTP suite. |
| FEAT-11-004 | portable automated | `CliModelGeneratorTests.Generate_RejectsAsyncVoidAndUnsupportedAsyncResultTypes` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Async-void and unsupported task-result declarations produced errors and no root generation in the focused generator run and strict 86-test MTP suite. |
| FEAT-11-004 | portable automated | `CliModelGeneratorTests.Generate_RejectsUnsupportedPackConstructorsWithoutRegisteringRoot` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Unsupported and ambiguous constructors were rejected in the focused Debug run and strict 124-test Release suite. |
| FEAT-11-005 | portable automated | `CliModelGeneratorTests.Generate_ConstrainsSynchronousEntrypointToSynchronousCommandTrees` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | The test passed in the 90-test strict MTP suite, including C# generic-constraint rejection for a root with an async child. |
| FEAT-14-001 | portable automated | `OptionPackTests.InvokeAsync_BindsNestedPackValuesAndGroupsHelpByNearestTitle` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-14-002 | portable automated | `OptionPackTests.Create_RejectsPackCyclesAndVisibleAliasCollisions`; `OptionPackTests.Parse_AllowsPackReuseOnDisjointCommandsWithoutSharingValues` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-14-004 | portable automated | `OptionPackTests.InvokeAsync_BindsNestedPackValuesAndGroupsHelpByNearestTitle`; `OptionPackTests.InvokeAsync_PreservesOmittedAndExplicitEmptyPackMetadata` | Mauve.Cli maintainers | .NET 11; no privileged environment | Help grouping and nullable/explicit-empty metadata passed in the strict 83-test MTP suite. |
| FEAT-14-005 | portable automated | `OptionPackTests.Parse_AllowsPackReuseOnDisjointCommandsWithoutSharingValues`; `OptionPackTests.Parse_CreatesIndependentCollectionValuesForEachPackParse`; `OptionPackTests.InvokeAsync_KeepsPackValuesIsolatedAcrossConcurrentInvocations` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-14-003 | portable automated | `CliModelGeneratorTests.Generate_BuildsPackModelEquivalentToExplicitModel`; `CliModelGeneratorTests.Generate_RejectsDuplicatePackAliasesWithoutRegisteringRoot`; `CliModelGeneratorTests.Generate_RejectsPackCyclesWithoutRegisteringRoot`; `CliModelGeneratorTests.Generate_AllowsPackTypeReuseOnDisjointCommands`; `CliModelGeneratorTests.Generate_RejectsDuplicateDirectPackTypesWithoutRegisteringRoot`; `CliModelGeneratorTests.Generate_RejectsUnsupportedPackPropertiesWithoutRegisteringRoot`; `CliModelGeneratorTests.Generate_InitializesPackPropertiesAfterItsConstructor` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Init-only declarations, construction, parity, lifecycle and rejection cases passed in the 42-test Release generator MTP run; older strict-suite evidence covers the previous declarative API. |
| FEAT-14-005 | portable automated | `CliModelGeneratorTests.Generate_InjectsInvocationLocalNestedPacksWithoutConstructingDuringParsing` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Generated nested pack instance and collection isolation across concurrent invocations, and no construction during parsing or invalid invocation, passed in the focused Debug run and strict 124-test Release suite. |
| FEAT-10-001 | portable automated | `CompletionServiceTests.CompleteAsync_SuggestsVisibleCommandsAndOptionsWithCursorRanges`; `CompletionServiceTests.CompleteAsync_PreservesOmittedAndExplicitEmptyDescriptions`; `CompletionServiceTests.CompleteAsync_UsesExplicitEnumMapsWithoutReflection`; `CompletionServiceTests.CompleteAsync_RespectsPendingOptionalBooleanAndEndMarkerStates`; `CompletionServiceTests.CompleteAsync_UsesNegativeNumericPrefixesAndNegatingAliasesAsValues` | Mauve.Cli maintainers | .NET 11; no privileged environment | Completion structure and nullable description metadata passed in the strict 83-test MTP suite. |
| FEAT-10-002 | portable automated | `CompletionServiceTests.CompleteAsync_ProvidesBoundedValuesFromDeclaredDependencies` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-10-003 | portable automated | `CompletionServiceTests.CompleteAsync_ReportsProviderFaultsAndBoundsCandidateWork`; `CompletionServiceTests.CompleteAsync_CancellationAndInvalidContextRemainExplicit` | Mauve.Cli maintainers | .NET 11; no privileged environment | Package-free harness passed; this is not product MSTest or Governance acceptance evidence. The test also passed in the strict 83-test MTP suite. |
| FEAT-10-004 | portable automated | `CompletionServiceTests.CompleteAsync_CancellationAndInvalidContextRemainExplicit`; `ShellCompletionDirectiveTests` | Mauve.Cli maintainers | .NET 11 | The core-only shell method remains unsupported; the opt-in directive has bounded shell adapters. See the separate real-shell method below. |
| FEAT-10-004 | environment-gated automated | `python3 scripts/verify-shells.py --require-all` | Mauve.Cli maintainers | Linux; Bash, Zsh, Fish, PowerShell, Python 3, base64, .NET 11 | Executed successfully on Linux x64 with Bash 5.3, Zsh 5.9, Fish 4.2.1 and PowerShell 7.5.4. Exercises registration, spaces, quotes, Unicode and hostile candidate insertion; Bash also exercises simple open quotes. Missing shells are unavailable, not passing evidence. Scope does not cover the full shell grammar. |
| FEAT-10-005 | portable automated | `CompletionCommandLineTests.ParseCommandLine_MapsQuotedEscapedAndWhitespacePositions` | Mauve.Cli maintainers | .NET 11 | Passed in a focused 13-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-10-005 | portable automated | `CompletionCommandLineTests.ParseCommandLine_TreatsPayloadAsTextAndRejectsInvalidCoordinates` | Mauve.Cli maintainers | .NET 11 | Passed in a focused 13-test Release MTP run; metadata-bearing TRX has not been imported. |
| FEAT-13-001 | portable automated | `AotAcceptanceTests.VerifyGeneratedAndExplicitScenarios`; `scripts/verify-aot.sh` | Mauve.Cli maintainers | .NET 11; focused AOT tests plus generated and explicit Commands examples | Managed and native focused tests passed; the published Commands examples contained no generator assembly in the 2026-11-22 run. |
| FEAT-13-002 | environment-gated automated | `AotAcceptanceTests.VerifyGeneratedAndExplicitScenarios`; `scripts/verify-aot.sh` managed trimmed publishes | Mauve.Cli maintainers | Native Linux x64; .NET SDK 11.0.100 | The managed focused test and both trimmed example outputs ran successfully; evidence and hashes are retained under `artifacts/aot-verification/linux-x64/`. |
| FEAT-13-003 | environment-gated automated | `scripts/verify-aot.sh` NativeAOT test and example publishes/executions | Mauve.Cli maintainers | Native Linux x64; Clang 21.1.8 | The native focused test reported exactly one pass with four requirement traits, and both native examples passed their model smoke checks. |
| FEAT-13-004 | environment-gated automated | FEAT-13 support matrix; ADR-013; native-host guard in `scripts/verify-aot.sh` | Mauve.Cli maintainers | Linux x64 verified; ARM64 and other RIDs unavailable/unverified | Only Linux x64 is claimed. Other targets remain explicitly unverified; cross-publish alone is rejected. |
| FEAT-15-001 | portable automated | `CommandModelTests.Create_PreservesRootDirectiveOrderAndRejectsDuplicateNames` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 99-test strict MTP suite. |
| FEAT-15-001 | portable automated | `CommandModelTests.Create_RejectsDirectiveRegistrationOnChildCommand` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 99-test strict MTP suite. |
| FEAT-15-002 | portable automated | `CliInvokerTests.InvokeAsync_RunsDirectivesInOrderBeforeOrdinaryInvalidOutcome` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 99-test strict MTP suite, including a root option before directives and the first child command. |
| FEAT-15-002 | portable automated | `CliInvokerTests.InvokeAsync_ValidatesAllDirectivesBeforeRunningAny` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 99-test strict MTP suite. |
| FEAT-15-002 | portable automated | `CliInvokerTests.InvokeAsync_DoesNotRecognizeDirectivesAfterTheFirstCommand` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 99-test strict MTP suite; the directive and command handlers remain undispatched. |
| FEAT-15-003 | portable automated | `CliInvokerTests.InvokeAsync_RunsDirectivesInOrderBeforeOrdinaryInvalidOutcome` | Mauve.Cli maintainers | .NET 11; no privileged environment | Ordered execution, successful stop code and command-handler suppression passed in the 99-test strict MTP suite. |
| FEAT-15-004 | portable automated | `CliModelGeneratorTests.Generate_RegistersStandaloneAndRootMethodDirectives` | Mauve.Cli maintainers | .NET 11; Roslyn generator test project | Passed in the 99-test strict MTP suite. |
| FEAT-15-004 | environment-gated automated | `AotAcceptanceTests.VerifyGeneratedAndExplicitDirectives`; `scripts/verify-aot.sh` | Mauve.Cli maintainers | Native Linux x64; .NET SDK 11.0.100 and native toolchain | Generated and explicit directive scenarios passed managed and NativeAOT execution; exact FEAT-15 evidence was imported by the gate. |
| FEAT-15-005 | portable automated | `HelpDocumentTests.Create_ProjectsDirectivesBeforeTheFirstCommandWithoutSensitiveValueNames` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 99-test strict MTP suite. |
| FEAT-15-005 | portable automated | `CompletionServiceTests.CompleteAsync_SuggestsDirectivesBeforeTheFirstCommand` | Mauve.Cli maintainers | .NET 11; no privileged environment | Passed in the 99-test strict MTP suite. |
| FEAT-15-005 | environment-gated automated | `AotAcceptanceTests.VerifyGeneratedAndExplicitDirectives`; `scripts/verify-aot.sh` | Mauve.Cli maintainers | Native Linux x64; .NET SDK 11.0.100 and native toolchain | Generated and explicit directive scenarios passed managed and NativeAOT execution; exact FEAT-15 evidence was imported by the gate. |
| FEAT-15-006 | portable automated | `PredefinedDirectivesTests.Debug_WaitsForAttachmentBeforeContinuing`; `PredefinedDirectivesTests.Debug_CancellationStopsWaitingAndLaterHandlers` | Mauve.Cli maintainers | .NET 11 | Passed in the 109-test strict MTP suite using an injected attachment probe, not a live debugger. |
| FEAT-15-008 | portable automated | `ShellCompletionDirectiveTests.Completion_EmitsRegistrationForEachSupportedShell`; `ShellCompletionDirectiveTests.Completion_DoesNotRegisterRetiredSuggestDirective`; `CliModelGeneratorTests.Generate_RegistersCompletionProvidersFromGeneratedModel` | Mauve.Cli maintainers | .NET 11 | Passed in the 116-test strict MTP suite. Shell execution is separately environment-gated under FEAT-10-004. |
| FEAT-15-008 | environment-gated automated | `scripts/verify-aot.sh` | Mauve.Cli maintainers | Linux x64; .NET 11 and native toolchain | Script syntax and protocol output from both trimmed and NativeAOT examples passed in `bash scripts/verify.sh`; this is not FEAT-15-008 TRX evidence. |

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

## Untrusted-input testing evidence (2026-11-24)

Separate from the ledger above; see `docs/untrusted-input-testing-audit.md`.

- Runtime Release suite: 182 passed, 0 failed/skipped; metadata-bearing TRX
  imported with `governance test-results` (`artifacts/test-results/untrusted-input/`).
- `bash scripts/verify.sh`: 233 tests passed (182 runtime, 45 generator, 6 AOT),
  all 30 fuzz seeds replayed, Governance checks and Linux x64 NativeAOT passed.
- Live fuzzing: 10-second smoke runs of `completion`, `shell`, `csv` and `argv`
  finished with no findings. A first run that instrumented the harness assembly
  too crashed at startup (`AccessViolationException`); `scripts/fuzz.sh` now
  instruments only `Mauve.Cli.dll`. These are smoke runs, not campaigns.
- Not run: real Bash/Zsh/Fish/PowerShell integration and Windows multicall
  normalization.
- Fuzz campaigns (300 seconds each, sequential, local bridge, default 4096-byte
  inputs) completed with no findings: `completion` 5,716,367 runs; `shell`
  1,827,556; `csv` 2,782,950; `argv` 1,676,014. Outputs are under the ignored
  `artifacts/fuzzing/campaign-<target>-300s/`. This is bounded evidence, not
  proof of absence of defects. (An earlier note called libFuzzer's `cov: 3`
  "coarse feedback"; that figure counts only the bridge's own counters. The
  `ft:` value reflects real SharpFuzz feedback.)

### Fuzz coverage expansion (2026-11-24)

Those campaigns exercised the original, narrower harness (about 45% of the
`Mauve.Cli` assembly's lines were executed when its generated corpus was
replayed). The harness was then extended with a rich public-API model (enum,
numeric, default, validator and collection options; directives; nested child
with positionals; providers), completion-service checks, and a typed
environment binding.

- Smoke runs of the extended harness found two real defects in
  `CompletionService`: `AddDirectiveCandidates` threw
  `ArgumentOutOfRangeException` for a cursor at offset 0 of a `[`-prefixed
  token, and `ConvertPositionals` threw `InvalidCastException` whenever a
  completed positional value preceded the active token. Both are fixed with
  regression tests (`CompleteAsync_HandlesCursorAtStartOfDirectiveToken`,
  `CompleteAsync_ConvertsCompletedPositionalValuesBeforeCompletingTheActiveToken`;
  each verified to fail without the fix) and regression seeds. Three further
  smoke failures were harness oracle false positives (round-trip and relaxed
  limit checks) and were corrected in the harness.
- Extended-harness campaigns (300 seconds each, run in parallel, same bridge,
  4096-byte inputs) completed with no findings: `completion` 3,256,192 runs;
  `shell` 1,795,435; `csv` 1,596,603; `argv` 1,190,039
  (`artifacts/fuzzing/campaign2-<target>-300s/`).
- Corpora were minimized with `libFuzzer -merge=1` and promoted as `gen-*`
  files: 1,123 committed inputs in total (completion 298, shell 297, csv 226,
  argv 302; about 37 KB). `bash scripts/replay-fuzz-corpus.sh all` replays all
  of them clean in one process per target.
- `scripts/fuzz-coverage.sh` (committed corpus, Release, `dotnet-coverage`
  18.12.0, ReportGenerator 5.6.1; whole `Mauve.Cli` assembly):

  | Corpus replayed | Line coverage | Branch coverage |
  | --- | --- | --- |
  | Original 30 seeds, old harness | 38.2% | 23.3% |
  | Original 30 seeds + old generated corpus (728), old harness | 45.2% | 33.3% |
  | Original 30 seeds, extended harness | 48.1% | 28.7% |
  | Committed corpus (1,123), extended harness | 65.9% | 63.2% |

  `CliParser` 61% to 87%, `CompletionService` 45% to 87%, `ShellCompletion`
  49%, `CliInvoker` 17% to 33%. Coverage is a reporting measure, not evidence
  of absence of defects; most remaining uncovered code is outside the fuzzed
  boundaries (declaration attributes, union converters, option packs and help
  rendering).
- Runtime Release suite after the fixes: 184 passed, 0 failed/skipped;
  metadata-bearing TRX imported with `governance test-results`
  (`artifacts/test-results/fuzz-coverage/`, FEAT-10 and FEAT-15 Passed).
  `bash scripts/verify.sh` passed with 235 tests.
