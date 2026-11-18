using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mauve.Cli.Completion;

/// <summary>An immutable completion outcome with explicit completeness status.</summary>
public sealed class CompletionResult
{
    private readonly ReadOnlyCollection<CompletionCandidate> _candidates;

    internal CompletionResult(
        CompletionOutcome outcome,
        IEnumerable<CompletionCandidate> candidates,
        bool isComplete,
        int? tokenIndex = null)
    {
        Outcome = outcome;
        _candidates = Array.AsReadOnly(candidates.ToArray());
        IsComplete = isComplete;
        TokenIndex = tokenIndex;
    }

    /// <summary>Gets the completion outcome.</summary>
    public CompletionOutcome Outcome { get; }

    /// <summary>Gets candidates yielded before the outcome was finalized.</summary>
    public IReadOnlyList<CompletionCandidate> Candidates => _candidates;

    /// <summary>Gets whether the candidate list is complete.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the original argument index when context resolution failed.</summary>
    public int? TokenIndex { get; }
}
