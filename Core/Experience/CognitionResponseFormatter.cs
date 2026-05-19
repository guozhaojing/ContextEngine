// =============================================================================
// Experience/CognitionResponseFormatter.cs — developer-friendly response formatting
// =============================================================================
// Determinism: formatting is a pure function of CognitionResult + options.
// Provenance: formatted output includes citations, evidence sections, and confidence.
// Replay: formatted output structure is deterministic.
// Grounding: confidence bar, citation blocks, and contradiction warnings are explicit.
// =============================================================================

using Core.Cognition;
using Core.Grounding.Confidence;

namespace Core.Experience;

public sealed class CognitionResponseFormatter
{
    private readonly ResponseFormatOptions _options;

    public CognitionResponseFormatter(ResponseFormatOptions? options = null)
    {
        _options = options ?? ResponseFormatOptions.Default;
    }

    public string Format(CognitionResult result)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"# {DeriveTitle(result)}");
        sb.AppendLine();

        if (_options.ShowConfidenceBar)
        {
            sb.AppendLine(FormatConfidenceBar(result.OverallConfidence));
            sb.AppendLine();
        }

        if (_options.ShowEvidenceSummary)
        {
            sb.AppendLine(FormatEvidenceSummary(result));
            sb.AppendLine();
        }

        foreach (var exp in result.Explanations.OrderBy(e => e.ConfidenceLevel)
            .ThenBy(e => e.ExplanationId, StringComparer.Ordinal))
        {
            sb.AppendLine(FormatExplanation(exp));
        }

        if (result.Citations.Count > 0 && _options.ShowCitations)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 引用来源");
            sb.AppendLine();

            var grouped = result.Citations
                .GroupBy(c => c.Layer)
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            var citeIdx = 1;
            foreach (var group in grouped)
            {
                if (!string.IsNullOrEmpty(group.Key))
                    sb.AppendLine($"### {group.Key}");

                foreach (var c in group.OrderBy(c => c.SourceNodeId, StringComparer.Ordinal))
                {
                    var scope = c.ConfidenceLevel switch
                    {
                        ConfidenceLevel.Certain => "&#x2705;", // check
                        ConfidenceLevel.Strong => "&#x2714;",  // heavy check
                        ConfidenceLevel.Moderate => "&#x26A0;", // warning
                        ConfidenceLevel.Weak => "&#x26A0;",     // warning
                        _ => "&#x274C;",                        // cross
                    };

                    var line = $"{citeIdx}. {scope} `{c.SourceNodeLabel}`";
                    if (!string.IsNullOrEmpty(c.SourceFile))
                    {
                        var shortFile = c.SourceFile.Length > 80
                            ? "..." + c.SourceFile[^77..]
                            : c.SourceFile;
                        line += $" — {shortFile}";
                    }
                    if (!string.IsNullOrEmpty(c.SymbolHandle))
                        line += $" ({c.SymbolHandle[..Math.Min(c.SymbolHandle.Length, 40)]}...)";

                    sb.AppendLine(line);
                    citeIdx++;
                }
            }
        }

        if (_options.ShowContradictionWarnings && result.OverallConfidence <= ConfidenceLevel.Moderate)
        {
            sb.AppendLine();
            sb.AppendLine("> **&#x26A0; 有限置信度**");
            sb.AppendLine("> 此解释的置信度为中或以下。证据不完整。");
            sb.AppendLine("> 建议对照源码直接验证。");
        }

        if (result.OverallConfidence >= ConfidenceLevel.Weak && result.Explanations.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("> &#x1F50D; 此查询可用的证据有限。");
            sb.AppendLine("> 代码图可能未包含足够信息。");
        }

        return sb.ToString();
    }

    public string FormatWithSummary(CognitionResult result, string query, string routingInfo)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"**查询:** {query}");
        sb.AppendLine($"**引擎:** {routingInfo}");
        sb.AppendLine($"**置信度:** {ConfidenceBar(result.OverallConfidence)}");
        sb.AppendLine($"**证据:** {result.EvidenceCount} 条引用");
        sb.AppendLine();
        sb.Append(Format(result));
        return sb.ToString();
    }

    private static string DeriveTitle(CognitionResult result)
        => result.ResultType switch
        {
            CognitionResultType.ArchitectureExplanation => "架构概览",
            CognitionResultType.ChangeImpactAnalysis => "变更影响分析",
            CognitionResultType.BusinessCapabilityMap => "业务能力映射",
            CognitionResultType.RootCauseAnalysis => "根因分析",
            _ => "代码分析",
        };

    private static string FormatConfidenceBar(ConfidenceLevel level)
    {
        var (label, bar) = ConfidenceBar(level);
        return $"**置信度:** {label}\n```\n{bar}\n```";
    }

    private static (string Label, string Bar) ConfidenceBar(ConfidenceLevel level)
        => level switch
        {
            ConfidenceLevel.Certain => ("确定", "[██████████] 100%"),
            ConfidenceLevel.Strong => ("高", "[████████░░]  85%"),
            ConfidenceLevel.Moderate => ("中", "[██████░░░░]  60%"),
            ConfidenceLevel.Weak => ("低", "[████░░░░░░]  40%"),
            ConfidenceLevel.Speculative => ("推测", "[██░░░░░░░░]  20%"),
            ConfidenceLevel.Unsupported => ("无证据", "[░░░░░░░░░░]   0%"),
            _ => ("未知", "[░░░░░░░░░░]"),
        };

    private static string FormatEvidenceSummary(CognitionResult result)
        => result.EvidenceCount > 0
            ? $"*{result.EvidenceCount} 个源码引用支持此解释。*"
            : "*无可用的源码引用。*";

    private static string FormatExplanation(GroundedExplanation exp)
    {
        var prefix = exp.ConfidenceLevel switch
        {
            ConfidenceLevel.Certain or ConfidenceLevel.Strong => "",
            ConfidenceLevel.Moderate => "&#x26A0; ",
            ConfidenceLevel.Weak or ConfidenceLevel.Speculative => "&#x26A0; ",
            _ => "&#x274C; ",
        };

        var text = $"{prefix}{exp.Text}";

        if (exp.SupportingNodeIds.Count > 0)
        {
            var fileSources = exp.SupportingSourceFiles
                .Where(f => !string.IsNullOrEmpty(f))
                .Select(f => f.Length > 60 ? "..." + Path.GetFileName(f) : f)
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToList();

            if (fileSources.Count > 0)
                text += $"  *({string.Join(", ", fileSources)})*";
        }

        return text;
    }
}

public class ResponseFormatOptions
{
    public bool ShowConfidenceBar { get; init; } = true;
    public bool ShowEvidenceSummary { get; init; } = true;
    public bool ShowCitations { get; init; } = true;
    public bool ShowContradictionWarnings { get; init; } = true;
    public bool UseEmoji { get; init; } = true;
    public int MaxExplanationsShown { get; init; } = 30;
    public int MaxCitationsShown { get; init; } = 20;

    public static ResponseFormatOptions Default => new();
}
