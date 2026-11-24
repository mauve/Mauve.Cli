# ADR-018: Multicall executable dispatch

## Status
Accepted for [FEAT-17](../Requirements/FEAT-17-Multicall-Dispatch.md).

## Context
A multicall binary, such as BusyBox, implements several operations in one
executable and selects behavior from argv zero, often using symlinks.
Mauve.Cli already supports separate validated root models but has no executable
name selector. A child command may need its own executable name without
becoming a separately declared root.

## Decision
- Add `CliInvoker.DispatchMulticallAsync`, not another `RunAsync` overload.
- Accept multiple `CommandModel` instances with implicit root name/alias
  routes and immutable `CliMulticallRoute` entries mapping extra executable
  filenames to full command paths.
- Read the invocation name from `CliHostOptions.ExecutableName`, preserving
  its argv-zero default and injectable host/test behavior. Never resolve the
  symlink or use the process image path.
- Use platform path basename extraction; Windows strips `.exe` and ignores
  ordinal casing. Other platforms use exact ordinal filenames. Resolve
  command path segments with the existing ordinal name/alias rules.
- Validate the entire dispatch configuration before selecting or parsing.
  Reject duplicate normalized executable keys and invalid paths rather than
  assigning precedence. Setup errors are not mapped as handler exceptions.
- Pass canonical initial ancestry into the internal parser instead of
  prepending command tokens. Preserve input coordinates, root directives,
  converters, inherited scope, environment prefixes, cancellation, and
  existing validated host dispatch.
- Render help usage and `$0` examples relative to the mapped entry command
  while retaining canonical ownership metadata. Do not rewrite non-executable
  examples or infer shell-adapter registration.
- Retain all model/generator hierarchy rules. No runtime reflection, new
  dependency, process-global state, signal handler, or process termination
  is needed.

## Consequences
Each call validates a table linear in registered names and resolves explicit
paths through supplied command models. Configuration is bounded by caller
collections and model limits; parser input limits still apply to actual argv.
The dedicated route type provides defensive path snapshots and discoverable
XML documentation without extending unrelated host settings.

Mapped child invocation must be tested with root directives, inherited
options, descendants, help, diagnostic coordinates, and real symlinks—not
merely injected filenames. Platform normalization tests do not imply that
Windows publication is verified. Linux x64 remains the executed AOT target.

Affected requirements:
[FEAT-17-001 through FEAT-17-004](../Requirements/FEAT-17-Multicall-Dispatch.md),
integrating with [FEAT-09](../Requirements/FEAT-09-Invocation-Lifecycle.md)
and [FEAT-13](../Requirements/FEAT-13-NativeAot-And-Trimming.md).
