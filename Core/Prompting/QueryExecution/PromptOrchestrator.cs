// =============================================================================
// QueryExecution/PromptOrchestrator.cs — main entry point for prompt generation
// =============================================================================
// 【设计】接收 StructuredContext，不直接调用 GraphQueryService / Retrieval Engine
// 【边界】只通过 ContextPolicy / PromptStrategy / PromptTemplate 组装 Prompt
// =============================================================================

using System.Diagnostics;
using System.Text;
using Core.Context.Budgeting;
using Core.Context.Models;
using Core.Prompting.ContextPolicies;
using Core.Prompting.Models;
using Core.Prompting.PromptStrategy;
using Core.Prompting.PromptTemplates;
using Core.QueryUnderstanding;

namespace Core.Prompting.QueryExecution;

public sealed class PromptOrchestrator
{
    private readonly PolicySelector _policySelector;
    private readonly Dictionary<QueryIntent, IPromptStrategy> _strategies;

    public PromptOrchestrator()
    {
        _policySelector = new PolicySelector();
        _strategies = new Dictionary<QueryIntent, IPromptStrategy>
        {
            [QueryIntent.FlowAnalysis] = new BugFixStrategy(),
            [QueryIntent.RouteLookup] = new FeatureImplementationStrategy(),
            [QueryIntent.ImpactAnalysis] = new RefactorStrategy(),
            [QueryIntent.EntityLookup] = new DataFlowStrategy(),
            [QueryIntent.ValidationLookup] = new ValidationStrategy()
        };
    }

    public OrchestrationResult Execute(string query, StructuredContext? context = null)
    {
        var sw = Stopwatch.StartNew();
        var trace = new List<TraceStep>();
        var intent = QueryIntentClassifier.Classify(query);

        trace.Add(Step("classify", "Intent Classification",
            $"Query classified as: {intent}", TraceStepStatus.Success, sw.ElapsedMilliseconds));

        var policy = _policySelector.Select(intent);
        trace.Add(Step("policy", "Policy Selection",
            $"Policy: {policy.PolicyName} ({policy.PolicyId})", TraceStepStatus.Success, sw.ElapsedMilliseconds));

        IPromptStrategy? strategy = null;
        if (_strategies.TryGetValue(intent, out var s))
        {
            strategy = s;
            trace.Add(Step("strategy", "Strategy Selection",
                $"Strategy: {strategy.StrategyName}", TraceStepStatus.Success, sw.ElapsedMilliseconds));
        }
        else
        {
            strategy = new FeatureImplementationStrategy();
            trace.Add(Step("strategy", "Strategy Selection",
                $"Intent {intent} has no dedicated strategy, using FeatureImplementationStrategy as fallback",
                TraceStepStatus.Warning, sw.ElapsedMilliseconds));
        }

        if (context is null)
        {
            trace.Add(Step("context", "Context Validation",
                "No StructuredContext provided. Creating minimal context from query.",
                TraceStepStatus.Warning, sw.ElapsedMilliseconds));
            context = CreateMinimalContext(query);
        }

        var strategyResult = strategy.Execute(context, policy);
        trace.Add(Step("execute", "Strategy Execution",
            $"Generated prompt with {strategyResult.FinalPrompt.Sections.Count} sections, ~{strategyResult.FinalPrompt.TokenEstimate} tokens",
            TraceStepStatus.Success, sw.ElapsedMilliseconds));

        var retrievalPolicy = RetrievalPolicy.FromIntent(
            intent switch
            {
                QueryIntent.FlowAnalysis => "bug",
                QueryIntent.RouteLookup => "feature",
                QueryIntent.ImpactAnalysis => "refactor",
                QueryIntent.EntityLookup => "data",
                QueryIntent.ValidationLookup => "validation",
                _ => "general"
            });

        sw.Stop();

        return new OrchestrationResult
        {
            FinalPrompt = strategyResult.FinalPrompt,
            Strategy = strategyResult.StrategyName,
            Template = strategyResult.Template.TemplateName,
            Policy = policy,
            RetrievalPolicy = retrievalPolicy,
            Trace = new PromptTrace
            {
                TraceId = $"trace-{DateTime.Now:yyyyMMddHHmmss}",
                Query = query,
                Steps = trace
            },
            Intent = intent,
            TokenEstimate = strategyResult.FinalPrompt.TokenEstimate,
            TotalElapsedMs = sw.ElapsedMilliseconds
        };
    }

    public OrchestrationResult Execute(StructuredContext context)
    {
        return Execute(context.Query, context);
    }

    private static TraceStep Step(string id, string phase, string description, TraceStepStatus status, long elapsedMs)
    {
        return new TraceStep
        {
            StepId = id,
            Phase = phase,
            Description = description,
            Status = status,
            ElapsedMs = elapsedMs,
            Details = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["timestamp"] = DateTime.Now.ToString("HH:mm:ss.fff")
            }
        };
    }

    private static StructuredContext CreateMinimalContext(string query)
    {
        return new StructuredContext
        {
            Query = query,
            Intent = "unknown",
            Summary = $"Minimal context for query: {query}",
            TokenEstimate = ContextBudgetEstimator.Estimate(query),
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "minimal_fallback",
                ["candidates"] = "0"
            }
        };
    }
}

public sealed class OrchestrationResult
{
    public required FinalPrompt FinalPrompt { get; init; }
    public required string Strategy { get; init; }
    public required string Template { get; init; }
    public required ContextPolicy Policy { get; init; }
    public required RetrievalPolicy RetrievalPolicy { get; init; }
    public required PromptTrace Trace { get; init; }
    public QueryIntent Intent { get; init; }
    public int TokenEstimate { get; init; }
    public long TotalElapsedMs { get; init; }
}
