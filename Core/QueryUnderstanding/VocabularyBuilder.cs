// =============================================================================
// QueryUnderstanding/VocabularyBuilder.cs — 项目词库构建器
// =============================================================================
// 从 CodeGraph（Nodes/Facts/Edges）和 CodeUnit 扫描结果中提取：
//   - Entity names（来自 nh:entity-access 事实）
//   - Table names（来自 nh:table 属性）
//   - Route paths（来自 http-route 事实）
//   - Method names / Class names（来自 CodeGraph.Nodes）
// 自动构建别名图：Table ↔ Entity ↔ Route ↔ Repository
// =============================================================================

using Core.Graph;
using Core.Graph.Analysis;
using Core.Models;
using Core.Scanning;

namespace Core.QueryUnderstanding;

public sealed class VocabularyBuilder
{
    private readonly QueryNormalizer _normalizer = new();

    public ProjectVocabulary Build(CodeGraph graph, SolutionScanResult? scan = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var vocab = new ProjectVocabulary
        {
            ScanRoot = graph.ScanRoot,
            GeneratedAt = DateTime.UtcNow
        };

        BuildEntities(vocab, graph);
        BuildTables(vocab, graph);
        BuildRoutes(vocab, graph);
        BuildClassesAndMethods(vocab, graph);
        BuildNormalizedTerms(vocab, graph, scan);
        BuildAliasGraph(vocab, graph);
        BuildSynonyms(vocab);

        return vocab;
    }

    private void BuildEntities(ProjectVocabulary vocab, CodeGraph graph)
    {
        var entityFacts = graph.Facts
            .Where(f => f.FactType == "nh-entity-access")
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var freqByClass = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var fact in entityFacts)
        {
            if (!fact.Data.TryGetValue("entityClass", out var entityClass)
                || string.IsNullOrEmpty(entityClass))
                continue;

            if (!freqByClass.ContainsKey(entityClass))
                freqByClass[entityClass] = 0;
            freqByClass[entityClass]++;

            if (!seen.Add(entityClass))
                continue;

            var normalized = _normalizer.NormalizeIdentifier(entityClass);
            var tokens = _normalizer.Tokenize(entityClass);

            vocab.Entities.Add(new VocabularyEntry
            {
                Original = entityClass,
                Normalized = normalized,
                Tokens = tokens,
                Kind = "entity",
                Frequency = freqByClass.GetValueOrDefault(entityClass, 0)
            });
        }

        // 也加入 Entity 节点（来自图节点中的外部节点）
        foreach (var node in graph.Nodes.Where(n => n.Kind == GraphNodeKind.Entity))
        {
            var className = node.ClassName;
            if (string.IsNullOrEmpty(className) || className == "unknown")
                continue;

            if (seen.Add(className))
            {
                var normalized = _normalizer.NormalizeIdentifier(className);
                vocab.Entities.Add(new VocabularyEntry
                {
                    Original = className,
                    Normalized = normalized,
                    Tokens = _normalizer.Tokenize(className),
                    Kind = "entity",
                    ProjectName = node.ProjectName
                });
            }
        }
    }

    private void BuildTables(ProjectVocabulary vocab, CodeGraph graph)
    {
        var tableSet = new HashSet<string>(StringComparer.Ordinal);

        // 从 Facts 中提取 table 属性
        foreach (var fact in graph.Facts.Where(f => f.FactType == "nh-entity-access"))
        {
            if (fact.Data.TryGetValue("table", out var table) && !string.IsNullOrEmpty(table))
            {
                if (tableSet.Add(table))
                {
                    vocab.Tables.Add(new VocabularyEntry
                    {
                        Original = table,
                        Normalized = _normalizer.NormalizeIdentifier(table),
                        Tokens = _normalizer.Tokenize(table),
                        Kind = "table"
                    });
                }
            }
        }

        // 从 Entity 外部节点 ID 中提取表名
        foreach (var node in graph.Nodes.Where(n => n.Kind == GraphNodeKind.Entity))
        {
            if (node.Attributes.TryGetValue("nh:table", out var table) && !string.IsNullOrEmpty(table))
            {
                if (tableSet.Add(table))
                {
                    vocab.Tables.Add(new VocabularyEntry
                    {
                        Original = table,
                        Normalized = _normalizer.NormalizeIdentifier(table),
                        Tokens = _normalizer.Tokenize(table),
                        Kind = "table",
                        ProjectName = node.ProjectName
                    });
                }
            }
        }

        // 从 Entity 节点 ID 解析表名 (ext::nh:entity::NS.Class::Table)
        foreach (var node in graph.Nodes.Where(n =>
            n.Id.StartsWith("ext::nh:entity", StringComparison.Ordinal)))
        {
            var parts = node.Id.Split("::");
            if (parts.Length >= 4)
            {
                var table = parts[^1];
                if (!string.IsNullOrEmpty(table) && table != "unknown" && tableSet.Add(table))
                {
                    vocab.Tables.Add(new VocabularyEntry
                    {
                        Original = table,
                        Normalized = _normalizer.NormalizeIdentifier(table),
                        Tokens = _normalizer.Tokenize(table),
                        Kind = "table",
                        ProjectName = node.ProjectName
                    });
                }
            }
        }
    }

    private void BuildRoutes(ProjectVocabulary vocab, CodeGraph graph)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fact in graph.Facts.Where(f => f.FactType == "http-route"))
        {
            if (!fact.Data.TryGetValue("route", out var route) || string.IsNullOrEmpty(route))
                continue;

            if (!seen.Add(route))
                continue;

            var cleanRoute = route.TrimStart('/');
            var normalizedRoute = _normalizer.NormalizeRoute(route);

            vocab.Routes.Add(new VocabularyEntry
            {
                Original = route,
                Normalized = normalizedRoute,
                Tokens = _normalizer.TokenizeRoute(route),
                Kind = "route"
            });

            // 也从 Controller + Action 提取
            if (fact.Data.TryGetValue("controller", out var controller) && seen.Add($"ctrl:{controller}"))
            {
                vocab.Classes.Add(new VocabularyEntry
                {
                    Original = controller,
                    Normalized = _normalizer.NormalizeIdentifier(controller),
                    Tokens = _normalizer.Tokenize(controller),
                    Kind = "controller"
                });
            }
        }
    }

    private void BuildClassesAndMethods(ProjectVocabulary vocab, CodeGraph graph)
    {
        var classSeen = new HashSet<string>(StringComparer.Ordinal);
        var methodSeen = new HashSet<string>(StringComparer.Ordinal);
        var methodFreq = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes.Where(n => n.Kind == GraphNodeKind.Method && !n.IsExternal))
        {
            var className = node.ClassName;
            var methodName = node.MethodName;

            if (!string.IsNullOrEmpty(className) && classSeen.Add(className))
            {
                vocab.Classes.Add(new VocabularyEntry
                {
                    Original = className,
                    Normalized = _normalizer.NormalizeIdentifier(className),
                    Tokens = _normalizer.Tokenize(className),
                    Kind = "class",
                    ProjectName = node.ProjectName
                });
            }

            if (!string.IsNullOrEmpty(methodName))
            {
                methodFreq[methodName] = methodFreq.GetValueOrDefault(methodName, 0) + 1;

                if (methodSeen.Add(methodName))
                {
                    vocab.Methods.Add(new VocabularyEntry
                    {
                        Original = methodName,
                        Normalized = _normalizer.NormalizeIdentifier(methodName),
                        Tokens = _normalizer.Tokenize(methodName),
                        Kind = "method",
                        ProjectName = node.ProjectName,
                        Frequency = methodFreq.GetValueOrDefault(methodName, 0)
                    });
                }
            }
        }

        // 更新所有 method entries 的 frequency
        foreach (var entry in vocab.Methods)
            entry.Frequency = methodFreq.GetValueOrDefault(entry.Original, 0);
    }

    private void BuildNormalizedTerms(ProjectVocabulary vocab, CodeGraph graph,
        SolutionScanResult? scan)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 从所有 vocabulary entries 收集词根
        foreach (var src in GetAllEntries(vocab))
        {
            foreach (var token in src.Tokens)
            {
                if (token.Length < 3)
                    continue;

                if (!seen.Add(token))
                    continue;

                vocab.NormalizedTerms.Add(new NormalizedTerm
                {
                    Original = src.Original,
                    Normalized = token.ToLowerInvariant(),
                    Source = src.Kind,
                    Components = _normalizer.SplitComponents(src.Original)
                });
            }
        }

        // 也处理 CodeUnit 中的类名和命名空间
        if (scan is not null)
        {
            foreach (var project in scan.Projects)
            {
                foreach (var unit in project.CodeUnits)
                {
                    foreach (var token in _normalizer.Tokenize(unit.Namespace))
                    {
                        if (token.Length < 3 || !seen.Add(token))
                            continue;
                        vocab.NormalizedTerms.Add(new NormalizedTerm
                        {
                            Original = unit.Namespace + "." + unit.ClassName,
                            Normalized = token.ToLowerInvariant(),
                            Source = "namespace",
                            Components = _normalizer.SplitComponents(unit.Namespace)
                        });
                    }
                }
            }
        }
    }

    private void BuildAliasGraph(ProjectVocabulary vocab, CodeGraph cg)
    {
        var aliasDict = vocab.AliasGraph;

        // ① Table ↔ Entity (from nh:entity-access facts)
        var entityTableMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fact in cg.Facts.Where(f => f.FactType == "nh-entity-access"))
        {
            if (fact.Data.TryGetValue("entityClass", out var entity) &&
                fact.Data.TryGetValue("table", out var table) &&
                !string.IsNullOrEmpty(entity) && !string.IsNullOrEmpty(table))
            {
                entityTableMap[entity] = table;
                AddToGraph(vocab, entity, table);
            }
        }

        // ② Entity ↔ Repository (from caller annotations)
        foreach (var node in cg.Nodes.Where(n => n.Kind == GraphNodeKind.Method))
        {
            if (node.Attributes.TryGetValue("entity", out var entity) &&
                !string.IsNullOrEmpty(entity))
            {
                var className = node.ClassName;
                if (!string.IsNullOrEmpty(className))
                {
                    AddToGraph(vocab, entity, className);
                }
            }
        }

        // ③ Controller → Route
        foreach (var fact in cg.Facts.Where(f => f.FactType == "http-route"))
        {
            if (fact.Data.TryGetValue("controller", out var controller) &&
                fact.Data.TryGetValue("route", out var route) &&
                !string.IsNullOrEmpty(controller) && !string.IsNullOrEmpty(route))
            {
                AddToGraph(vocab, controller, route);
            }
        }
    }

    /// <summary>
    /// 从实体名称和常规模式构造同义词。
    /// 包括：中文↔英文映射、命名规范变体。
    /// </summary>
    private void BuildSynonyms(ProjectVocabulary vocab)
    {
        var synonyms = vocab.Synonyms;

        // 从实体名产生同义词 (EQA_EquipGRelation → equip, relation)
        foreach (var entity in vocab.Entities)
        {
            foreach (var token in entity.Tokens)
            {
                if (token.Length >= 3)
                {
                    synonyms.AddMapping(entity.Original, token);

                    // 添加常见的业务概念映射
                    MapBusinessConcept(synonyms, token);
                }
            }
        }

        // 从表名产生同义词
        foreach (var table in vocab.Tables)
        {
            foreach (var token in table.Tokens)
            {
                if (token.Length >= 3)
                    synonyms.AddMapping(table.Original, token);
            }
        }

        // 路径段 → 路由
        foreach (var route in vocab.Routes)
        {
            foreach (var token in route.Tokens)
            {
                if (token.Length >= 3)
                    synonyms.AddMapping(route.Original, token);
            }
        }

        // 跨条目别名：同名 token 互相映射
        var tokenToOriginals = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in GetAllEntries(vocab))
        {
            foreach (var token in entry.Tokens)
            {
                if (token.Length < 3)
                    continue;

                if (!tokenToOriginals.ContainsKey(token))
                    tokenToOriginals[token] = new HashSet<string>(StringComparer.Ordinal);
                tokenToOriginals[token].Add(entry.Original);
            }
        }

        // 对同 token 的所有原始名称建立双向映射
        foreach (var (_, originals) in tokenToOriginals)
        {
            if (originals.Count <= 1)
                continue;

            foreach (var a in originals)
                foreach (var b in originals)
                    if (!string.Equals(a, b, StringComparison.Ordinal))
                        synonyms.AddBidirectionalMapping(a, b);
        }
    }

    private static void MapBusinessConcept(SynonymMap synonyms, string token)
    {
        var lower = token.ToLowerInvariant();
        switch (lower)
        {
            case "reagent":
                synonyms.AddMapping(lower, "试剂");
                synonyms.AddMapping("试剂", lower);
                break;
            case "equip":
            case "equipment":
                synonyms.AddMapping(lower, "设备");
                synonyms.AddMapping("设备", lower);
                break;
            case "lab":
            case "laboratory":
                synonyms.AddMapping(lower, "实验室");
                synonyms.AddMapping("实验室", lower);
                break;
            case "sample":
                synonyms.AddMapping(lower, "样本");
                synonyms.AddMapping("样本", lower);
                break;
            case "test":
                synonyms.AddMapping(lower, "检验");
                synonyms.AddMapping("检验", lower);
                break;
            case "report":
                synonyms.AddMapping(lower, "报告");
                synonyms.AddMapping("报告", lower);
                break;
            case "quality":
                synonyms.AddMapping(lower, "质量");
                synonyms.AddMapping("质量", lower);
                break;
            case "control":
                synonyms.AddMapping(lower, "控制");
                synonyms.AddMapping("控制", lower);
                break;
            case "item":
                synonyms.AddMapping(lower, "项目");
                synonyms.AddMapping("项目", lower);
                break;
            case "result":
                synonyms.AddMapping(lower, "结果");
                synonyms.AddMapping("结果", lower);
                break;
            case "audit":
                synonyms.AddMapping(lower, "审核");
                synonyms.AddMapping("审核", lower);
                break;
            case "relation":
                synonyms.AddMapping(lower, "关系");
                synonyms.AddMapping("关系", lower);
                break;
            case "group":
                synonyms.AddMapping(lower, "组");
                synonyms.AddMapping("组", lower);
                break;
            case "specialty":
                synonyms.AddMapping(lower, "专业");
                synonyms.AddMapping("专业", lower);
                break;
            case "department":
                synonyms.AddMapping(lower, "科室");
                synonyms.AddMapping("科室", lower);
                break;
            case "doctor":
                synonyms.AddMapping(lower, "医生");
                synonyms.AddMapping("医生", lower);
                break;
            case "patient":
                synonyms.AddMapping(lower, "患者");
                synonyms.AddMapping("患者", lower);
                break;
        }
    }

    private static void AddToGraph(ProjectVocabulary vocab, string nodeA, string nodeB)
    {
        if (!vocab.AliasGraph.ContainsKey(nodeA))
            vocab.AliasGraph[nodeA] = new List<string>();
        var aList = vocab.AliasGraph[nodeA];
        if (!aList.Contains(nodeB))
            aList.Add(nodeB);

        if (!vocab.AliasGraph.ContainsKey(nodeB))
            vocab.AliasGraph[nodeB] = new List<string>();
        var bList = vocab.AliasGraph[nodeB];
        if (!bList.Contains(nodeA))
            bList.Add(nodeA);
    }

    private static IEnumerable<VocabularyEntry> GetAllEntries(ProjectVocabulary vocab)
    {
        foreach (var e in vocab.Entities) yield return e;
        foreach (var e in vocab.Tables) yield return e;
        foreach (var e in vocab.Routes) yield return e;
        foreach (var e in vocab.Classes) yield return e;
        foreach (var e in vocab.Methods) yield return e;
    }
}
