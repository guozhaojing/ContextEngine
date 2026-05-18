// =============================================================================
// ContextBudget.cs — token budget tracking with category support
// =============================================================================

using Core.Context.Budgeting;

namespace Core.Context;

public sealed class ContextBudget
{
    public int MaxTokens { get; init; } = 12000;
    public int AllocatedTokens { get; private set; }
    public int Remaining => MaxTokens - AllocatedTokens;

    public IReadOnlyDictionary<string, (int Max, int Allocated, int Remaining)>? CategorySnapshot { get; private set; }

    public bool CanAllocate(int tokens) => AllocatedTokens + tokens <= MaxTokens;

    public bool TryAllocate(int tokens)
    {
        if (!CanAllocate(tokens)) return false;
        AllocatedTokens += tokens;
        return true;
    }

    public void SetCategorySnapshot(TokenBudgetAllocator allocator)
    {
        CategorySnapshot = allocator.GetSnapshot();
    }

    public ContextBudget Clone()
    {
        return new ContextBudget
        {
            MaxTokens = MaxTokens,
            AllocatedTokens = AllocatedTokens,
            CategorySnapshot = CategorySnapshot
        };
    }
}

public enum TruncationStrategy
{
    TrimTail,
    Summarize,
    Drop
}

public static class ContextBudgetDefaults
{
    public const int DefaultMaxTokens = 12000;
    public const int MaxSectionTokens = 4096;
    public const int SummaryTokens = 512;
}
