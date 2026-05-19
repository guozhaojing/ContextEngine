// =============================================================================
// GenericResolution/DaoCallSiteResolver.cs — BLL→DAO 调用 Entity 传播 (Roslyn)
// =============================================================================
// 【Strict】只在 BLL 方法体内检测到对 DAO 字段的实际调用时才绑定 Entity。
//   使用 Roslyn InvocationExpressionSyntax + MemberAccessExpressionSyntax 替代 regex。
//   禁止：无调用时自动绑定、跨方法链传播、低置信度传播。
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Graph.Analysis.GenericResolution;

public sealed class DaoCallSiteResolver
{
    private readonly EntityClassRegistry _registry;

    public DaoCallSiteResolver(EntityClassRegistry registry)
    {
        _registry = registry;
    }

    public List<DaoCallSite> Resolve(
        string methodContent,
        Dictionary<string, DaoFieldMatch> daoFields,
        string bllClassName)
    {
        if (daoFields.Count == 0)
            return new List<DaoCallSite>();

        var tree = CSharpSyntaxTree.ParseText(
            $"class _Wrapper {{ void _M() {{ {methodContent} }} }}");
        var root = tree.GetCompilationUnitRoot();

        var methodBody = root.DescendantNodes()
            .OfType<BlockSyntax>()
            .FirstOrDefault();

        if (methodBody is null)
            return new List<DaoCallSite>();

        return Resolve(methodBody, daoFields, bllClassName);
    }

    public List<DaoCallSite> Resolve(
        BlockSyntax methodBody,
        Dictionary<string, DaoFieldMatch> daoFields,
        string bllClassName)
    {
        var results = new List<DaoCallSite>();

        if (daoFields.Count == 0)
            return results;

        foreach (var (_, fieldMatch) in daoFields)
        {
            var entityName = fieldMatch.EntityName;

            if (entityName is null)
            {
                var binding = _registry.GetBindingForClass(fieldMatch.DaoClassName);
                if (binding is not null)
                    entityName = binding.EntityType;
            }

            if (entityName is null)
                continue;

            var calls = FindCallsOnField(methodBody, fieldMatch.FieldName);

            foreach (var call in calls)
            {
                results.Add(new DaoCallSite
                {
                    BllClassName = bllClassName,
                    DaoFieldName = fieldMatch.FieldName,
                    DaoClassName = fieldMatch.DaoClassName,
                    EntityName = entityName,
                    CalledMethod = call,
                    Confidence = fieldMatch.Confidence
                });
            }
        }

        return results;
    }

    public List<DaoCallSite> Resolve(
        SyntaxNode? methodNode,
        Dictionary<string, DaoFieldMatch> daoFields,
        string bllClassName)
    {
        if (methodNode is null)
            return new List<DaoCallSite>();

        if (methodNode is BlockSyntax block)
            return Resolve(block, daoFields, bllClassName);

        var root = CSharpSyntaxTree.ParseText(methodNode.ToString()).GetCompilationUnitRoot();
        var blockBody = root.DescendantNodes().OfType<BlockSyntax>().FirstOrDefault();
        if (blockBody is null)
            return new List<DaoCallSite>();

        return Resolve(blockBody, daoFields, bllClassName);
    }

    private static HashSet<string> FindCallsOnField(BlockSyntax body, string fieldName)
    {
        var calls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var invoc in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invoc.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Expression is IdentifierNameSyntax identifier
                    && identifier.Identifier.Text == fieldName)
                {
                    calls.Add(memberAccess.Name.Identifier.Text);
                    continue;
                }

                if (memberAccess.Expression is MemberAccessExpressionSyntax innerAccess
                    && innerAccess.Expression is ThisExpressionSyntax
                    && innerAccess.Name.Identifier.Text == fieldName)
                {
                    calls.Add(memberAccess.Name.Identifier.Text);
                    continue;
                }
            }
        }

        return calls;
    }
}

public sealed class DaoCallSite
{
    public string BllClassName { get; set; } = "";
    public string DaoFieldName { get; set; } = "";
    public string DaoClassName { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string CalledMethod { get; set; } = "";
    public GenericResolutionConfidence Confidence { get; set; }
}
