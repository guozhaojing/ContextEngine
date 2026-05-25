// =============================================================================
// SemanticDoc/SyntaxWalkers.cs — lightweight Roslyn walkers for extraction
// =============================================================================
// Purpose: Extract structured info from method bodies without full SemanticModel.
// Level: SyntaxWalker only — no binding, no type resolution. Fast.
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Core.Cognition.SemanticDoc;

public sealed class MethodBodyWalker : CSharpSyntaxWalker
{
    public List<string> SqlStrings { get; } = new();
    public List<string> StringLiterals { get; } = new();
    public List<string> ExceptionTypes { get; } = new();
    public List<string> DtoTypes { get; } = new();
    public List<string> CalledMethodNames { get; } = new();
    public List<string> AttributeNames { get; } = new();
    public int CatchCount { get; set; }
    public int ThrowCount { get; set; }

    public MethodBodyWalker() : base(SyntaxWalkerDepth.Trivia) { }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.Kind() == SyntaxKind.StringLiteralExpression)
        {
            var text = node.Token.ValueText;
            if (!string.IsNullOrEmpty(text))
            {
                StringLiterals.Add(text);
                if (ContainsSqlPattern(text))
                    SqlStrings.Add(text);
            }
        }
        base.VisitLiteralExpression(node);
    }

    public override void VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        foreach (var part in node.Contents)
        {
            if (part is InterpolatedStringTextSyntax text)
                StringLiterals.Add(text.TextToken.ValueText);
        }
        base.VisitInterpolatedStringExpression(node);
    }

    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        CatchCount++;
        if (node.Declaration is not null)
            ExceptionTypes.Add(node.Declaration.Type.ToString());
        base.VisitCatchClause(node);
    }

    public override void VisitThrowStatement(ThrowStatementSyntax node)
    {
        ThrowCount++;
        if (node.Expression is ObjectCreationExpressionSyntax creation)
            ExceptionTypes.Add(creation.Type.ToString());
        base.VisitThrowStatement(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is MemberAccessExpressionSyntax member)
            CalledMethodNames.Add(member.Name.Identifier.Text);
        else if (node.Expression is IdentifierNameSyntax ident)
            CalledMethodNames.Add(ident.Identifier.Text);
        base.VisitInvocationExpression(node);
    }

    public override void VisitAttribute(AttributeSyntax node)
    {
        AttributeNames.Add(node.Name.ToString());
        base.VisitAttribute(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        var typeName = node.Type?.ToString() ?? "";
        if (IsDtoLike(typeName))
            DtoTypes.Add(typeName);
        base.VisitParameter(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var typeName = node.Type.ToString();
        if (IsDtoLike(typeName))
            DtoTypes.Add(typeName);
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var typeName = node.Type.ToString();
        if (IsDtoLike(typeName))
            DtoTypes.Add(typeName);
        base.VisitPropertyDeclaration(node);
    }

    private static bool ContainsSqlPattern(string text)
    {
        return text.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
            || text.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
            || text.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("FROM", StringComparison.OrdinalIgnoreCase)
            || text.Contains("WHERE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("JOIN", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDtoLike(string typeName)
    {
        return typeName.EndsWith("DTO", StringComparison.OrdinalIgnoreCase)
            || typeName.EndsWith("Dto", StringComparison.Ordinal)
            || typeName.EndsWith("VO", StringComparison.Ordinal)
            || typeName.EndsWith("Result", StringComparison.Ordinal)
            || typeName.EndsWith("Request", StringComparison.Ordinal)
            || typeName.EndsWith("Response", StringComparison.Ordinal);
    }
}
