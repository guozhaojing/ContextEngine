// =============================================================================
// Semantics/SemanticCallResolver.cs — enhanced call resolution with SymbolHandle
// =============================================================================
// Wraps InvocationSemanticResolver with SymbolHandle output.
// Produces both ResolvedMethodInfo (for compatibility) and SymbolHandle (for grounding).
// Handles:
//   - Overload resolution with parameter type check
//   - Extension method resolution (ReducedFrom)
//   - Generic method type argument capture
// =============================================================================

using Core.Truth;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Semantics;

public static class SemanticCallResolver
{
    public sealed record ResolvedCall(
        ResolvedMethodInfo MethodInfo,
        SymbolHandle Handle,
        bool IsOverloaded,
        bool IsExtensionMethod,
        EvidenceStrength Evidence);

    public static ResolvedCall Resolve(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var methodSymbol = SemanticSymbolResolver.ResolveMethodSymbol(invocation, semanticModel, cancellationToken);
        var methodInfo = InvocationSemanticResolver.Resolve(invocation, semanticModel, cancellationToken);

        if (methodSymbol is null)
        {
            return new ResolvedCall(
                methodInfo,
                SymbolHandle.Empty,
                false,
                false,
                EvidenceStrength.None
            );
        }

        var handle = SymbolHandle.FromMethod(methodSymbol);
        var isOverloaded = SemanticSymbolResolver.IsOverloaded(invocation, semanticModel);
        var isExtension = SemanticSymbolResolver.IsExtensionMethodInvocation(invocation, semanticModel);

        var evidence = DetermineEvidence(methodSymbol, methodInfo);

        return new ResolvedCall(
            methodInfo,
            handle,
            isOverloaded,
            isExtension,
            evidence
        );
    }

    public static SymbolHandle ResolveHandle(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken = default)
    {
        var call = Resolve(invocation, semanticModel, cancellationToken);
        return call.Handle;
    }

    private static EvidenceStrength DetermineEvidence(IMethodSymbol method, ResolvedMethodInfo info)
    {
        if (method.Locations.Any(l => l.IsInSource))
            return EvidenceStrength.SemanticDirect;

        if (info.IsExternal)
            return EvidenceStrength.SyntaxDirect;

        return EvidenceStrength.SemanticInferred;
    }
}
