using Core.Graph;

namespace Core.Export;

public static class LayerInference
{
    public const string Route = "route";
    public const string Controller = "controller";
    public const string Service = "service";
    public const string Repository = "repository";
    public const string Entity = "entity";
    public const string Table = "table";
    public const string Method = "method";

    public static string InferNodeLayer(GraphNode node)
    {
        if (node.Attributes.TryGetValue("aspnet-route:entry-point", out _))
            return Route;

        if (node.Attributes.TryGetValue("route", out _))
            return Route;

        if (node.Kind == GraphNodeKind.Entity)
            return Entity;

        if (node.Kind == GraphNodeKind.Table)
            return Table;

        if (node.Kind == GraphNodeKind.External)
        {
            if (node.Id.Contains("::nh:entity::", StringComparison.Ordinal))
                return Entity;
            if (node.Id.Contains("::table::", StringComparison.Ordinal))
                return Table;
        }

        var className = node.ClassName ?? "";
        if (className.EndsWith("Controller", StringComparison.Ordinal))
            return Controller;
        if (className.EndsWith("Service", StringComparison.Ordinal))
            return Service;
        if (className.EndsWith("BLL", StringComparison.Ordinal))
            return Service;
        if (className.EndsWith("Manager", StringComparison.Ordinal))
            return Service;
        if (className.EndsWith("Provider", StringComparison.Ordinal))
            return Service;
        if (className.EndsWith("Handler", StringComparison.Ordinal))
            return Service;
        if (className.EndsWith("Repository", StringComparison.Ordinal))
            return Repository;
        if (className.EndsWith("Dao", StringComparison.Ordinal))
            return Repository;
        if (className.EndsWith("DaoNHB", StringComparison.Ordinal))
            return Repository;
        if (className.EndsWith("DAO", StringComparison.Ordinal))
            return Repository;
        if (className.Contains("Dao", StringComparison.Ordinal))
            return Repository;

        return Method;
    }

    public static string InferEdgeLayer(string edgeKind)
    {
        if (edgeKind == GraphEdgeKinds.Call)
            return EdgeLayer.Call;

        if (edgeKind.StartsWith("spring:", StringComparison.Ordinal))
            return EdgeLayer.Framework;

        if (edgeKind.StartsWith("nh:", StringComparison.Ordinal))
            return EdgeLayer.Data;

        if (edgeKind.StartsWith("transaction", StringComparison.Ordinal))
            return EdgeLayer.Transaction;

        return EdgeLayer.Call;
    }

    public static string GetNodeType(string layer)
    {
        return layer switch
        {
            Route => "routeNode",
            Controller => "controllerNode",
            Service => "serviceNode",
            Repository => "repositoryNode",
            Entity => "entityNode",
            Table => "tableNode",
            _ => "methodNode"
        };
    }

    public static readonly IReadOnlyList<string> DefaultLayerOrder = new[]
    {
        Route, Controller, Service, Repository, Entity, Table
    };
}
