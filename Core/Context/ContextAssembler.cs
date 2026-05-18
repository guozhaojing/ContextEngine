using Core.Context.Assembly;
using Core.Context.Export;
using Core.Context.Models;
using Core.Retrieval.Embedding;
using Core.Retrieval.Retrieval;

namespace Core.Context;

public sealed class ContextAssembler
{
    private readonly ContextBudget _budget;
    private readonly ContextBuilder _builder;
    private readonly Assembly.ContextAssembler _pipelineAssembler;

    public ContextAssembler(ContextBuilder builder, int maxTokens = 12000)
    {
        _builder = builder;
        _budget = new ContextBudget { MaxTokens = maxTokens };
        _pipelineAssembler = new Assembly.ContextAssembler(
            builder.QueryService,
            new ContextAssemblyOptions { MaxTokens = maxTokens });
    }

    public ContextDocument Assemble(
        string documentId,
        RetrievalResult retrievalResult,
        int? maxTokens = null)
    {
        var budget = maxTokens.HasValue
            ? new ContextBudget { MaxTokens = maxTokens.Value }
            : _budget;

        var allSections = _builder.BuildSections(retrievalResult);

        var ordered = allSections.OrderByDescending(s => s.Priority).ToList();

        var included = new List<ContextSection>();
        var budgetClone = budget.Clone();

        foreach (var section in ordered)
        {
            var tokens = section.TokenCount;

            if (budgetClone.TryAllocate(tokens))
            {
                included.Add(section);
            }
            else if (section.Priority >= 7 && budgetClone.Remaining > 200)
            {
                var available = budgetClone.Remaining;
                var truncated = TruncateSection(section, available);
                if (truncated is not null && budgetClone.TryAllocate(truncated.TokenCount))
                {
                    included.Add(truncated);
                }
            }
        }

        return new ContextDocument
        {
            Id = documentId,
            Query = retrievalResult.Query.Query,
            Sections = included,
            BudgetMax = budget.MaxTokens,
            BudgetUsed = budget.AllocatedTokens,
            SourceResultCount = retrievalResult.Candidates.Count
        };
    }

    public StructuredContext AssembleStructured(RetrievalResult retrievalResult)
    {
        return _pipelineAssembler.Assemble(retrievalResult);
    }

    public async Task<string> ExportContextJsonAsync(
        RetrievalResult retrievalResult,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        var structured = _pipelineAssembler.Assemble(retrievalResult);
        var exportService = new ContextExportService();
        return await exportService.SaveAsync(structured, outputDirectory, ct);
    }

    private static ContextSection? TruncateSection(ContextSection section, int maxTokens)
    {
        if (maxTokens < 50) return null;

        var lines = section.Content.Split('\n');
        var sb = new System.Text.StringBuilder();
        var tokenBudget = maxTokens - 40;

        foreach (var line in lines)
        {
            var lineTokens = TokenEstimator.Estimate(line);
            if (tokenBudget - lineTokens < 0) break;
            sb.AppendLine(line);
            tokenBudget -= lineTokens;
        }

        sb.AppendLine($"\n[...truncated for budget, {maxTokens - tokenBudget} tokens]");

        var newContent = sb.ToString();
        return new ContextSection
        {
            Title = section.Title + " (truncated)",
            Content = newContent,
            Kind = section.Kind,
            Priority = section.Priority,
            TokenCount = TokenEstimator.Estimate(newContent),
            SourceChunkIds = section.SourceChunkIds,
            CompressionRatio = section.CompressionRatio,
            RelevanceScore = section.RelevanceScore
        };
    }
}
