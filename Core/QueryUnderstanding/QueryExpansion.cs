// =============================================================================
// QueryUnderstanding/QueryExpansion.cs — 查询扩展引擎
// =============================================================================
// 用户 Query "试剂" → 扩展:
//   reagent, EQA_Reagent, reagent service, reagent dao, reagent relation
// 策略:
//   1. Synonym lookup (中文→英文映射)
//   2. Vocabulary match (模糊匹配项目词库)
//   3. Compound expansion (多词组合)
//   4. Suffix template (entity / service / dao / repository / controller)
// =============================================================================

namespace Core.QueryUnderstanding;

public sealed class QueryExpansion
{
    private readonly QueryNormalizer _normalizer = new();

    private static readonly string[] SuffixTemplates =
    {
        "", " Service", " Dao", " Repository", " Controller",
        " Entity", " Manager", " ServiceImpl", " DAO"
    };

    private static readonly string[] DataLayerSuffixes =
    {
        " Service", " Dao", " Repository", " Entity", " DAO"
    };

    /// <summary>扩展查询 token 为候选术语集合。</summary>
    public QueryExpansionResult Expand(
        string query,
        ProjectVocabulary vocabulary,
        AliasGraph aliasGraph)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(aliasGraph);

        var result = new QueryExpansionResult
        {
            OriginalQuery = query,
            QueryTokens = QueryIntentClassifier.Tokenize(query).ToList()
        };

        foreach (var token in result.QueryTokens)
        {
            if (token.Length < 2)
                continue;

            var candidates = new List<ExpansionCandidate>();

            // ① 同义词扩展（含中文→英文）
            ExpandSynonyms(token, vocabulary, candidates);

            // ② 词库精确匹配
            ExpandVocabulary(token, vocabulary, candidates);

            // ③ 前缀模糊匹配
            ExpandFuzzy(token, vocabulary, candidates);

            // ④ 后缀模板扩展
            ExpandSuffixTemplates(token, vocabulary, candidates);

            // ⑤ 别名图扩展
            ExpandAliasGraph(token, vocabulary, aliasGraph, candidates);

            result.TokenExpansions[token] = candidates
                .DistinctBy(c => c.Term, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(c => c.Score)
                .Take(10)
                .ToList();
        }

        // ⑥ 复合扩展（跨 token）
        ExpandCompound(result, vocabulary);

        return result;
    }

    private void ExpandSynonyms(
        string token,
        ProjectVocabulary vocabulary,
        List<ExpansionCandidate> candidates)
    {
        var synonyms = vocabulary.Synonyms.ExpandToProjectTerms(token);
        foreach (var syn in synonyms)
        {
            candidates.Add(new ExpansionCandidate
            {
                Term = syn,
                Source = "synonym",
                Score = 0.9
            });
        }

        // 反向：用同义词映射表查找
        var reverseSynonyms = vocabulary.Synonyms.GetSynonyms(token);
        foreach (var s in reverseSynonyms)
        {
            candidates.Add(new ExpansionCandidate
            {
                Term = s,
                Source = "synonym-reverse",
                Score = 0.85
            });
        }
    }

    private void ExpandVocabulary(
        string token,
        ProjectVocabulary vocabulary,
        List<ExpansionCandidate> candidates)
    {
        var entries = GetAllEntries(vocabulary);
        var lowerToken = token.ToLowerInvariant();

        foreach (var entry in entries)
        {
            // 精确匹配 normalized 或 tokens
            if (string.Equals(entry.Normalized, lowerToken, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(new ExpansionCandidate
                {
                    Term = entry.Original,
                    Source = $"vocab:{entry.Kind}",
                    Score = 1.0,
                    Kind = entry.Kind
                });
            }

            foreach (var tok in entry.Tokens)
            {
                if (string.Equals(tok, lowerToken, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(new ExpansionCandidate
                    {
                        Term = entry.Original,
                        Source = $"vocab-token:{entry.Kind}",
                        Score = 0.95,
                        Kind = entry.Kind
                    });
                    break;
                }
            }
        }
    }

    private void ExpandFuzzy(
        string token,
        ProjectVocabulary vocabulary,
        List<ExpansionCandidate> candidates)
    {
        var entries = GetAllEntries(vocabulary);
        var lowerToken = token.ToLowerInvariant();

        foreach (var entry in entries)
        {
            foreach (var tok in entry.Tokens)
            {
                if (tok.Length < 3 || lowerToken.Length < 3)
                    continue;

                // 前缀匹配
                if (tok.StartsWith(lowerToken, StringComparison.OrdinalIgnoreCase)
                    || lowerToken.StartsWith(tok, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(new ExpansionCandidate
                    {
                        Term = entry.Original,
                        Source = $"fuzzy-prefix:{entry.Kind}",
                        Score = 0.7,
                        Kind = entry.Kind
                    });
                    break;
                }

                // 子串包含
                if (tok.Contains(lowerToken, StringComparison.OrdinalIgnoreCase)
                    || lowerToken.Contains(tok, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(new ExpansionCandidate
                    {
                        Term = entry.Original,
                        Source = $"fuzzy-contain:{entry.Kind}",
                        Score = 0.5,
                        Kind = entry.Kind
                    });
                    break;
                }
            }
        }
    }

    private void ExpandSuffixTemplates(
        string token,
        ProjectVocabulary vocabulary,
        List<ExpansionCandidate> candidates)
    {
        var lowerToken = token.ToLowerInvariant();

        foreach (var suffix in SuffixTemplates)
        {
            var term = lowerToken + suffix.ToLowerInvariant();

            // 检查词库中是否确实存在（精确匹配）
            var exists = GetAllEntries(vocabulary)
                .Any(e => string.Equals(e.Normalized, term, StringComparison.OrdinalIgnoreCase)
                    || e.Tokens.Any(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase)));

            if (exists)
            {
                var fullTerm = char.ToUpperInvariant(lowerToken[0]) + lowerToken[1..] + suffix;
                candidates.Add(new ExpansionCandidate
                {
                    Term = fullTerm,
                    Source = "suffix-template",
                    Score = 0.6,
                    Kind = "expanded"
                });
            }
        }
    }

    private void ExpandAliasGraph(
        string token,
        ProjectVocabulary vocabulary,
        AliasGraph aliasGraph,
        List<ExpansionCandidate> candidates)
    {
        // 查找与 token 匹配的实体
        var matchedEntities = aliasGraph.FindByName(token).ToList();
        if (matchedEntities.Count == 0)
        {
            // 尝试通过 normalized 名匹配
            foreach (var entity in aliasGraph.Entities.Values)
            {
                var normalizedEnt = _normalizer.NormalizeIdentifier(entity.Name);
                if (normalizedEnt.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || entity.Aliases.Any(a => a.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedEntities.Add(entity);
                }
            }
        }

        foreach (var entity in matchedEntities)
        {
            var aliases = aliasGraph.ExpandToAliases(entity.Id, maxDepth: 2);
            foreach (var aliasId in aliases)
            {
                var aliasedEntity = aliasGraph.FindById(aliasId);
                if (aliasedEntity is not null && !string.IsNullOrEmpty(aliasedEntity.Name))
                {
                    candidates.Add(new ExpansionCandidate
                    {
                        Term = aliasedEntity.Name,
                        Source = $"alias:{aliasedEntity.Kind}",
                        Score = 0.8,
                        Kind = aliasedEntity.Kind.ToString().ToLowerInvariant()
                    });
                }
            }
        }
    }

    private void ExpandCompound(
        QueryExpansionResult result,
        ProjectVocabulary vocabulary)
    {
        if (result.QueryTokens.Count < 2)
            return;

        var entries = GetAllEntries(vocabulary);
        var queryLower = result.QueryTokens
            .Where(t => t.Length >= 2)
            .Select(t => t.ToLowerInvariant())
            .ToList();

        foreach (var entry in entries)
        {
            var normalizedLower = entry.Normalized.ToLowerInvariant();
            var matchCount = queryLower.Count(qt => normalizedLower.Contains(qt));

            if (matchCount >= queryLower.Count * 0.5 && matchCount >= 2)
            {
                result.CompoundExpansions.Add(new ExpansionCandidate
                {
                    Term = entry.Original,
                    Source = $"compound:{entry.Kind}",
                    Score = 0.85 + 0.05 * matchCount,
                    Kind = entry.Kind
                });
            }
        }
    }

    private static IEnumerable<VocabularyEntry> GetAllEntries(ProjectVocabulary vocabulary)
    {
        foreach (var e in vocabulary.Entities) yield return e;
        foreach (var e in vocabulary.Tables) yield return e;
        foreach (var e in vocabulary.Routes) yield return e;
        foreach (var e in vocabulary.Classes) yield return e;
        foreach (var e in vocabulary.Methods) yield return e;
    }
}

public sealed class QueryExpansionResult
{
    public string OriginalQuery { get; set; } = "";

    public List<string> QueryTokens { get; set; } = new();

    public Dictionary<string, List<ExpansionCandidate>> TokenExpansions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<ExpansionCandidate> CompoundExpansions { get; set; } = new();
}

public sealed class ExpansionCandidate
{
    public string Term { get; set; } = "";

    public string Source { get; set; } = "";

    public double Score { get; set; }

    public string? Kind { get; set; }
}
