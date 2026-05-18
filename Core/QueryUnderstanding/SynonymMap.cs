// =============================================================================
// QueryUnderstanding/SynonymMap.cs — 同义词/别名映射
// =============================================================================
// 将用户自然语言词汇映射到项目中的实际术语。
// 例如："试剂" → reagent, EQA_Reagent, EQA_EquipGRelation
// =============================================================================

namespace Core.QueryUnderstanding;

public sealed class SynonymMap
{
    private readonly Dictionary<string, HashSet<string>> _synonyms =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> Mappings
    {
        get
        {
            return _synonyms.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void AddMapping(string term, string synonym)
    {
        if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(synonym))
            return;

        var key = term.Trim().ToLowerInvariant();
        if (!_synonyms.ContainsKey(key))
            _synonyms[key] = new HashSet<string>(StringComparer.Ordinal);
        _synonyms[key].Add(synonym);

        // 反向索引（用于从项目术语查找用户语言）
        var reverseKey = synonym.Trim().ToLowerInvariant();
        if (!_synonyms.ContainsKey(reverseKey))
            _synonyms[reverseKey] = new HashSet<string>(StringComparer.Ordinal);
        _synonyms[reverseKey].Add(term);
    }

    public IReadOnlySet<string> GetSynonyms(string term)
    {
        var key = term.Trim().ToLowerInvariant();
        if (_synonyms.TryGetValue(key, out var synonyms))
            return synonyms;

        return new HashSet<string>(StringComparer.Ordinal);
    }

    public bool HasSynonym(string term)
    {
        return _synonyms.ContainsKey(term.Trim().ToLowerInvariant());
    }

    /// <summary>扩展用户词汇到项目术语。</summary>
    public IReadOnlyList<string> ExpandToProjectTerms(string userToken)
    {
        var key = userToken.Trim().ToLowerInvariant();
        if (!_synonyms.TryGetValue(key, out var terms))
            return Array.Empty<string>();

        return terms.ToArray();
    }

    public void AddBidirectionalMapping(string termA, string termB)
    {
        AddMapping(termA, termB);
        AddMapping(termB, termA);
    }

    public int Count => _synonyms.Count;
}
