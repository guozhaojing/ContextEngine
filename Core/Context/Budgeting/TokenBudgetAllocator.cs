// =============================================================================
// Budgeting/TokenBudgetAllocator.cs — category-based token allocation
// =============================================================================

namespace Core.Context.Budgeting;

public sealed class TokenBudgetAllocator
{
    private readonly Dictionary<string, BudgetCategory> _categories;
    private bool _locked;

    public TokenBudgetAllocator(int totalBudget = 12000)
    {
        TotalBudget = totalBudget;
        _categories = new Dictionary<string, BudgetCategory>(StringComparer.Ordinal)
        {
            ["routes"] = new BudgetCategory("Routes", (int)(totalBudget * 0.10)),
            ["semantic_paths"] = new BudgetCategory("Semantic Paths", (int)(totalBudget * 0.20)),
            ["business_rules"] = new BudgetCategory("Business Rules", (int)(totalBudget * 0.15)),
            ["methods"] = new BudgetCategory("Methods", (int)(totalBudget * 0.40)),
            ["metadata"] = new BudgetCategory("Metadata", (int)(totalBudget * 0.15))
        };
    }

    public int TotalBudget { get; }
    public int AllocatedTokens => _categories.Values.Sum(c => c.Allocated);
    public int RemainingTokens => TotalBudget - AllocatedTokens;

    public bool TryAllocate(string category, int tokens)
    {
        if (_locked) return false;
        if (!_categories.TryGetValue(category, out var budget)) return false;
        if (!budget.CanAllocate(tokens)) return false;
        budget.Allocate(tokens);
        return true;
    }

    public bool CanAllocate(string category, int tokens)
    {
        if (_locked) return false;
        return _categories.TryGetValue(category, out var budget) && budget.CanAllocate(tokens);
    }

    public int GetBudget(string category)
    {
        return _categories.TryGetValue(category, out var budget) ? budget.MaxTokens : 0;
    }

    public int GetRemaining(string category)
    {
        return _categories.TryGetValue(category, out var budget) ? budget.Remaining : 0;
    }

    public double GetUsageRatio(string category)
    {
        return _categories.TryGetValue(category, out var budget) ? budget.UsageRatio : 0;
    }

    public void Lock()
    {
        _locked = true;
    }

    public IReadOnlyDictionary<string, (int Max, int Allocated, int Remaining)> GetSnapshot()
    {
        return _categories.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.MaxTokens, kv.Value.Allocated, kv.Value.Remaining));
    }

    private sealed class BudgetCategory
    {
        public string Name { get; }
        public int MaxTokens { get; }
        public int Allocated { get; private set; }
        public int Remaining => MaxTokens - Allocated;
        public double UsageRatio => MaxTokens > 0 ? (double)Allocated / MaxTokens : 0;

        public BudgetCategory(string name, int maxTokens)
        {
            Name = name;
            MaxTokens = maxTokens;
        }

        public bool CanAllocate(int tokens) => Allocated + tokens <= MaxTokens;

        public void Allocate(int tokens)
        {
            Allocated += tokens;
        }
    }
}
