using System.Collections.Generic;
using System.Threading;

namespace Mauve.Cli.Completion;

/// <summary>Provides bounded asynchronous values for one completion target.</summary>
/// <param name="context">The safe partial completion context.</param>
/// <param name="cancellationToken">The caller-owned cancellation token.</param>
/// <returns>An asynchronous sequence of logical value candidates.</returns>
public delegate IAsyncEnumerable<CompletionValue> CompletionProvider(
    CompletionContext context,
    CancellationToken cancellationToken);
