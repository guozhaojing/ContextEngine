// =============================================================================
// Compression/MethodCompressor.cs — compress method source for context
// =============================================================================

using System.Text;
using Core.Context.Models;

namespace Core.Context.Compression;

public static class MethodCompressor
{
    private static readonly HashSet<string> CollapsibleKeywords = new(StringComparer.Ordinal)
    {
        "using", "namespace", "#region", "#endregion", "#pragma"
    };

    private static readonly HashSet<string> CriticalConditionMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "throw", "Validate", "Check", "Ensure", "Guard", "Authorize",
        "Audit", "Log", "Save", "Update", "Delete", "Insert", "Commit",
        "Rollback", "BeginTransaction", "Query", "GetById", "FindBy"
    };

    public static ContextCompressionResult Compress(string methodContent, IReadOnlyList<string> sourceChunkIds)
    {
        if (string.IsNullOrWhiteSpace(methodContent))
        {
            return new ContextCompressionResult
            {
                OriginalContent = methodContent,
                CompressedContent = "",
                OriginalTokens = 0,
                CompressedTokens = 0,
                Strategy = "empty",
                SourceChunkIds = sourceChunkIds
            };
        }

        var lines = methodContent.Split('\n');
        var sb = new StringBuilder();
        var signatureFound = false;
        var trivialCount = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line))
            {
                sb.AppendLine();
                continue;
            }

            if (IsCollapsible(line))
                continue;

            if (!signatureFound && IsMethodSignature(line))
            {
                signatureFound = true;
                sb.AppendLine(line);
                continue;
            }

            if (!signatureFound)
            {
                sb.AppendLine(line);
                continue;
            }

            if (IsCriticalStatement(line))
            {
                if (trivialCount > 0)
                {
                    sb.AppendLine($"  // ... {trivialCount} trivial statements collapsed");
                    trivialCount = 0;
                }
                sb.AppendLine(line);
            }
            else if (IsEntityAccess(line) || IsMethodInvocation(line))
            {
                sb.AppendLine(line);
            }
            else if (IsConditional(line))
            {
                sb.AppendLine(line);
            }
            else
            {
                trivialCount++;
            }
        }

        if (trivialCount > 0)
        {
            sb.AppendLine($"  // ... {trivialCount} trivial statements collapsed");
        }

        var compressed = sb.ToString().TrimEnd();
        var originalTokens = Budgeting.ContextBudgetEstimator.Estimate(methodContent);
        var compressedTokens = Budgeting.ContextBudgetEstimator.Estimate(compressed);

        return new ContextCompressionResult
        {
            OriginalContent = methodContent,
            CompressedContent = compressed,
            OriginalTokens = originalTokens,
            CompressedTokens = compressedTokens,
            Strategy = "MethodCompressor",
            SourceChunkIds = sourceChunkIds
        };
    }

    private static bool IsCollapsible(string line)
    {
        foreach (var kw in CollapsibleKeywords)
        {
            if (line.StartsWith(kw, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsMethodSignature(string line)
    {
        return (line.Contains("public ") || line.Contains("private ") ||
                line.Contains("protected ") || line.Contains("internal ") ||
                line.Contains("static ") || line.Contains("virtual ") ||
                line.Contains("override ") || line.Contains("async ")) &&
               line.Contains('(') && line.Contains(')') &&
               (line.EndsWith('{') || line.EndsWith(';'));
    }

    private static bool IsCriticalStatement(string line)
    {
        foreach (var method in CriticalConditionMethods)
        {
            if (line.Contains(method, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsEntityAccess(string line)
    {
        return line.Contains("Session", StringComparison.Ordinal) ||
               line.Contains("session", StringComparison.Ordinal) ||
               line.Contains("Repository", StringComparison.Ordinal) ||
               line.Contains(".Query<", StringComparison.Ordinal) ||
               line.Contains(".Get<", StringComparison.Ordinal) ||
               line.Contains(".Save(", StringComparison.Ordinal) ||
               line.Contains(".Update(", StringComparison.Ordinal) ||
               line.Contains(".Delete(", StringComparison.Ordinal) ||
               line.Contains("_dao.", StringComparison.Ordinal) ||
               line.Contains("_repo.", StringComparison.Ordinal) ||
               line.Contains("_repository.", StringComparison.Ordinal);
    }

    private static bool IsMethodInvocation(string line)
    {
        var trimmed = line.TrimStart();
        return !trimmed.StartsWith("if", StringComparison.Ordinal) &&
               !trimmed.StartsWith("for", StringComparison.Ordinal) &&
               !trimmed.StartsWith("while", StringComparison.Ordinal) &&
               !trimmed.StartsWith("switch", StringComparison.Ordinal) &&
               !trimmed.StartsWith("return", StringComparison.Ordinal) &&
               !trimmed.StartsWith("var ", StringComparison.Ordinal) &&
               !trimmed.StartsWith("int ", StringComparison.Ordinal) &&
               !trimmed.StartsWith("string ", StringComparison.Ordinal) &&
               !trimmed.StartsWith("bool ", StringComparison.Ordinal) &&
               !trimmed.StartsWith("List<", StringComparison.Ordinal) &&
               line.Contains('(') && line.Contains(')') && line.EndsWith(';');
    }

    private static bool IsConditional(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("if ", StringComparison.Ordinal) ||
               t.StartsWith("if(", StringComparison.Ordinal) ||
               t.StartsWith("switch", StringComparison.Ordinal) ||
               t.StartsWith("foreach", StringComparison.Ordinal) ||
               t.StartsWith("for ", StringComparison.Ordinal) ||
               t.StartsWith("while", StringComparison.Ordinal) ||
               t.StartsWith("return", StringComparison.Ordinal);
    }
}
