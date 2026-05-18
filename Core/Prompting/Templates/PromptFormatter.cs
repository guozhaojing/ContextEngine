// =============================================================================
// Templates/PromptFormatter.cs — base formatter interface for prompt output
// =============================================================================

using System.Text;
using Core.Prompting.Models;

namespace Core.Prompting.Templates;

public abstract class PromptFormatter
{
    protected readonly PromptAssemblyOptions Options;

    protected PromptFormatter(PromptAssemblyOptions? options = null)
    {
        Options = options ?? PromptAssemblyOptions.Default;
    }

    public abstract string Format(PromptContext context);

    protected static string FormatSectionHeader(string title, int priority, int tokens)
    {
        return $"\n## {title}\n<!-- priority={priority} tokens={tokens} -->\n";
    }

    protected static string TruncateToTokens(string content, int maxTokens, string suffix = "\n...truncated")
    {
        var tokens = EstimateTokens(content);
        if (tokens <= maxTokens) return content;

        var lines = content.Split('\n');
        var sb = new StringBuilder();
        var currentTokens = 0;

        foreach (var line in lines)
        {
            var lineTokens = EstimateTokens(line);
            if (currentTokens + lineTokens > maxTokens - EstimateTokens(suffix))
                break;
            sb.AppendLine(line);
            currentTokens += lineTokens;
        }

        sb.Append(suffix);
        return sb.ToString();
    }

    protected static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var chineseChars = 0;
        var englishChars = 0;
        var codeChars = 0;

        foreach (var c in text)
        {
            if (c is >= '\u4e00' and <= '\u9fff')
                chineseChars++;
            else if (c is '(' or ')' or '{' or '}' or ';' or '.' or ',' or '>' or '<' or '/' or '\\' or '_' or ':')
                codeChars++;
            else if (!char.IsWhiteSpace(c))
                englishChars++;
        }

        var chineseTokens = chineseChars;
        var englishTokens = englishChars / 4;
        var codeTokens = codeChars / 2;

        var total = chineseTokens + englishTokens + codeTokens;
        return Math.Max(total, 1);
    }
}
