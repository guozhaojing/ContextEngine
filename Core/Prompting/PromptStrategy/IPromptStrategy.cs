// =============================================================================
// PromptStrategy/IPromptStrategy.cs — strategy interface for prompt assembly
// =============================================================================

using Core.Context.Models;
using Core.Prompting.ContextPolicies;
using Core.Prompting.Models;
using Core.QueryUnderstanding;

namespace Core.Prompting.PromptStrategy;

public interface IPromptStrategy
{
    QueryIntent SupportedIntent { get; }
    string StrategyName { get; }
    PromptStrategyResult Execute(StructuredContext context, ContextPolicy policy);
}
