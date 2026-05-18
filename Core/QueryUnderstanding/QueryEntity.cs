// =============================================================================
// QueryUnderstanding/QueryEntity.cs — 查询实体（Table ↔ Entity ↔ Route ↔ Repo）
// =============================================================================
// AliasGraph 节点：每个节点代表一个命名实体（表/实体类/路由/Repository）。
// 边表示命名等价关系，支持双向遍历。
// =============================================================================

namespace Core.QueryUnderstanding;

public sealed class QueryEntity
{
    public string Id { get; set; } = "";

    public QueryEntityKind Kind { get; set; }

    public string Name { get; set; } = "";

    public List<string> Aliases { get; set; } = new();

    public Dictionary<string, string> Attributes { get; set; } = new(StringComparer.Ordinal);
}

public enum QueryEntityKind
{
    Table,
    Entity,
    Route,
    Repository,
    Controller
}

public sealed class AliasGraph
{
    private readonly Dictionary<string, QueryEntity> _entitiesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _nameIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _aliasEdges = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, QueryEntity> Entities => _entitiesById;

    public void AddEntity(QueryEntity entity)
    {
        if (string.IsNullOrEmpty(entity.Id))
            return;

        if (!_entitiesById.TryAdd(entity.Id, entity))
            return;

        IndexName(entity.Id, entity.Name, entity.Kind);
        foreach (var alias in entity.Aliases)
            IndexName(entity.Id, alias, entity.Kind);
    }

    public void AddAlias(string sourceId, string targetId)
    {
        if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId))
            return;

        if (!_aliasEdges.ContainsKey(sourceId))
            _aliasEdges[sourceId] = new HashSet<string>(StringComparer.Ordinal);
        _aliasEdges[sourceId].Add(targetId);

        if (!_aliasEdges.ContainsKey(targetId))
            _aliasEdges[targetId] = new HashSet<string>(StringComparer.Ordinal);
        _aliasEdges[targetId].Add(sourceId);
    }

    public QueryEntity? FindById(string id)
    {
        _entitiesById.TryGetValue(id, out var entity);
        return entity;
    }

    public IReadOnlyList<QueryEntity> FindByName(string name)
    {
        if (!_nameIndex.TryGetValue(name, out var ids))
            return Array.Empty<QueryEntity>();

        return ids.Select(id => _entitiesById.TryGetValue(id, out var e) ? e : null)
            .Where(e => e is not null)
            .ToList()!;
    }

    public IReadOnlyList<QueryEntity> FindByKind(QueryEntityKind kind)
    {
        return _entitiesById.Values.Where(e => e.Kind == kind).ToList();
    }

    public IReadOnlyList<string> GetAliases(string id)
    {
        if (!_aliasEdges.TryGetValue(id, out var aliases))
            return Array.Empty<string>();

        return aliases.ToList();
    }

    public IReadOnlyList<string> ExpandToAliases(string id, int maxDepth = 2)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((id, 0));
        visited.Add(id);

        var result = new List<string>();

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= maxDepth)
                continue;

            if (!_aliasEdges.TryGetValue(current, out var neighbors))
                continue;

            foreach (var neighbor in neighbors)
            {
                if (visited.Add(neighbor))
                {
                    result.Add(neighbor);
                    queue.Enqueue((neighbor, depth + 1));
                }
            }
        }

        return result;
    }

    private void IndexName(string entityId, string name, QueryEntityKind kind)
    {
        if (string.IsNullOrEmpty(name))
            return;

        if (!_nameIndex.ContainsKey(name))
            _nameIndex[name] = new HashSet<string>(StringComparer.Ordinal);
        _nameIndex[name].Add(entityId);
    }

    public static AliasGraph FromVocabulary(ProjectVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        var aliasGraph = new AliasGraph();

        var entityIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in vocabulary.Entities)
            entityIds[entry.Original] = AddOrGetEntity(aliasGraph, entry.Original, QueryEntityKind.Entity, entityIds);

        foreach (var entry in vocabulary.Tables)
            entityIds[entry.Original] = AddOrGetEntity(aliasGraph, entry.Original, QueryEntityKind.Table, entityIds);

        foreach (var entry in vocabulary.Routes)
            entityIds[entry.Original] = AddOrGetEntity(aliasGraph, entry.Original, QueryEntityKind.Route, entityIds);

        foreach (var entry in vocabulary.Classes)
            entityIds[entry.Original] = AddOrGetEntity(aliasGraph, entry.Original, QueryEntityKind.Controller, entityIds);

        foreach (var entry in vocabulary.Methods)
            entityIds[entry.Original] = AddOrGetEntity(aliasGraph, entry.Original, QueryEntityKind.Repository, entityIds);

        foreach (var (source, targets) in vocabulary.AliasGraph)
        {
            var sourceId = entityIds.GetValueOrDefault(source);
            if (sourceId is null)
                continue;

            foreach (var target in targets)
            {
                var targetId = entityIds.GetValueOrDefault(target);
                if (targetId is not null)
                    aliasGraph.AddAlias(sourceId, targetId);
            }
        }

        return aliasGraph;
    }

    private static string AddOrGetEntity(
        AliasGraph graph, string name, QueryEntityKind kind,
        Dictionary<string, string> idMap)
    {
        if (idMap.TryGetValue(name, out var existingId))
            return existingId;

        var id = $"{kind.ToString().ToLowerInvariant()}::{name}";
        idMap[name] = id;

        graph.AddEntity(new QueryEntity
        {
            Id = id,
            Kind = kind,
            Name = name,
            Aliases = new List<string> { name.ToLowerInvariant() }
        });

        return id;
    }
}
