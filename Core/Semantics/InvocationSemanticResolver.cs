// =============================================================================
// Semantics/InvocationSemanticResolver.cs — Roslyn 调用语义解析器
// =============================================================================
// 【边界】只做 GetSymbolInfo，不依赖图、不查询、不存储。
// 输入：InvocationExpressionSyntax + SemanticModel
// 输出：ResolvedMethodInfo
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Semantics;

public static class InvocationSemanticResolver
{
    /// <summary>
    /// 解析一次方法调用（如 _repo.Save()、this.Run()、Helper.Go()）的真实目标符号。
    /// </summary>
    public static ResolvedMethodInfo Resolve(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        // 核心 API：获取调用表达式绑定的符号
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var symbol = symbolInfo.Symbol;

        // 重载候选时取第一个（后续可改进为歧义标记）
        if (symbol is null && symbolInfo.CandidateSymbols.Length > 0)
            symbol = symbolInfo.CandidateSymbols[0];

        if (symbol is IMethodSymbol methodSymbol)
            return FromMethodSymbol(methodSymbol);

        // 委托字段调用等 fallback
        var expressionMethod = TryResolveFromExpression(invocation, semanticModel, cancellationToken);
        if (expressionMethod is not null)
            return expressionMethod;

        return CreateUnresolved(invocation);
    }

    private static ResolvedMethodInfo FromMethodSymbol(IMethodSymbol method)
    {
        // 扩展方法的 ReducedFrom 指向真正的静态扩展定义
        var targetMethod = method.MethodKind == MethodKind.ReducedExtension && method.ReducedFrom is not null
            ? method.ReducedFrom
            : method;

        var containingType = targetMethod.ContainingType;
        var namespaceName = GetNamespace(containingType);
        var className = GetClassName(containingType);

        return new ResolvedMethodInfo
        {
            Namespace = namespaceName,
            ClassName = className,
            MethodName = targetMethod.Name,
            ParameterTypes = targetMethod.Parameters
                .Select(p => p.Type.ToDisplayString())
                .ToList(),
            IsExternal = IsExternalMethod(targetMethod)
        };
    }

    private static ResolvedMethodInfo? TryResolveFromExpression(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionInfo = semanticModel.GetSymbolInfo(invocation.Expression, cancellationToken);
        var symbol = expressionInfo.Symbol ?? expressionInfo.CandidateSymbols.FirstOrDefault();

        return symbol switch
        {
            IMethodSymbol methodSymbol => FromMethodSymbol(methodSymbol),
            IFieldSymbol { Type: { TypeKind: TypeKind.Delegate } } fieldSymbol
                when fieldSymbol.Type is INamedTypeSymbol delegateType
                => FromDelegateInvoke(delegateType, invocation),
            _ => null
        };
    }

    private static ResolvedMethodInfo FromDelegateInvoke(
        INamedTypeSymbol delegateType,
        InvocationExpressionSyntax invocation)
    {
        var invokeMethod = delegateType.DelegateInvokeMethod;
        if (invokeMethod is not null)
            return FromMethodSymbol(invokeMethod);

        return CreateUnresolved(invocation);
    }

    /// <summary>所有 Location 都不在源码中 → 外部程序集。</summary>
    private static bool IsExternalMethod(IMethodSymbol method)
    {
        if (method.Locations.IsDefaultOrEmpty)
            return true;

        return method.Locations.All(location => !location.IsInSource);
    }

    private static string GetNamespace(INamedTypeSymbol? containingType)
    {
        var namespaceSymbol = containingType?.ContainingNamespace;
        if (namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace)
            return "";

        return namespaceSymbol.ToDisplayString();
    }

    private static string GetClassName(INamedTypeSymbol? containingType)
    {
        if (containingType is null)
            return "";

        if (containingType.ContainingType is not null)
            return $"{GetClassName(containingType.ContainingType)}.{containingType.Name}";

        return containingType.Name;
    }

    private static ResolvedMethodInfo CreateUnresolved(InvocationExpressionSyntax invocation)
    {
        var methodName = ExtractMethodName(invocation.Expression.ToString());

        return new ResolvedMethodInfo
        {
            Namespace = "",
            ClassName = "",
            MethodName = methodName,
            IsExternal = true
        };
    }

    private static string ExtractMethodName(string expression)
    {
        var name = expression.Trim();
        var paren = name.IndexOf('(');
        if (paren >= 0)
            name = name[..paren];

        var generic = name.IndexOf('<');
        if (generic >= 0)
            name = name[..generic];

        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }
}
