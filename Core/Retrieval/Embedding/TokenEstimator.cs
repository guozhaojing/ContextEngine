namespace Core.Retrieval.Embedding;

public static class TokenEstimator
{
    public static int Estimate(string text)
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

    public static int EstimateFromChunk(string content, int keywordCount)
    {
        var contentTokens = Estimate(content);
        var keywordTokens = keywordCount * 2;
        return contentTokens + keywordTokens;
    }
}
