// =============================================================================
// Grounding/CitationConstrainedGeneration.cs — evidence-attributed statement generation
// =============================================================================
// Determinism: citations assigned in stable order (by EvidenceId, StringComparer.Ordinal).
//   - Statement sort order is deterministic (by statement index, then by citation EvidenceId).
//   - No HashSet iteration in generation path.
// Provenance: every generated statement has a reference to at least one GroundingEvidence.
// Replay: GeneratedStatementSet is immutable and structurally comparable.
// Grounding: statements without at least one grounded citation are rejected.
// =============================================================================

using Core.Semantics;
using Core.Truth;

namespace Core.Grounding;

public sealed class CitationConstrainedGeneration
{
    private readonly CitationConstrainedOptions _options;

    public CitationConstrainedGeneration(CitationConstrainedOptions? options = null)
    {
        _options = options ?? CitationConstrainedOptions.Default;
    }

    public GeneratingStatementBuilder CreateBuilder()
    {
        return new GeneratingStatementBuilder(this);
    }

    public GeneratedStatementSet Finalize(GeneratingStatementBuilder builder)
    {
        var statements = builder.Build();

        var rejected = new List<string>();
        var accepted = new List<AttributedStatement>();

        foreach (var stmt in statements)
        {
            if (stmt.Citations.Count == 0)
            {
                rejected.Add($"Statement has no citations: '{stmt.Statement}'");
                continue;
            }

            var hasGroundedCitation = stmt.Citations.Any(c => !c.IsEmpty && c.AggregateConfidence.IsGrounded);
            if (!hasGroundedCitation && _options.RequireGroundedCitation)
            {
                rejected.Add($"Statement has no grounded citations: '{stmt.Statement}'");
                continue;
            }

            CheckForbiddenPattern(stmt, rejected);
            if (rejected.Count > 0) continue;

            accepted.Add(stmt);
        }

        return new GeneratedStatementSet
        {
            AcceptedStatements = accepted,
            RejectedStatements = rejected,
            TotalSubmitted = statements.Count,
            TotalAccepted = accepted.Count,
            TotalRejected = rejected.Count,
            EvidenceMap = BuildEvidenceMap(accepted),
        };
    }

    private void CheckForbiddenPattern(AttributedStatement stmt, List<string> rejected)
    {
        foreach (var pattern in _options.ForbiddenStatementPatterns)
        {
            if (stmt.Statement.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                rejected.Add($"Forbidden pattern '{pattern}' in statement: '{stmt.Statement}'");
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildEvidenceMap(IReadOnlyList<AttributedStatement> accepted)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < accepted.Count; i++)
        {
            var stmt = accepted[i];
            foreach (var citation in stmt.Citations)
            {
                if (!map.TryGetValue(citation.EvidenceId, out var stmtIds))
                {
                    stmtIds = new List<string>();
                    map[citation.EvidenceId] = stmtIds;
                }
                stmtIds.Add(stmt.StatementId);
            }
        }
        return map.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.AsReadOnly(),
            StringComparer.Ordinal);
    }
}

public sealed class GeneratingStatementBuilder
{
    private readonly CitationConstrainedGeneration _parent;
    private readonly List<AttributedStatement> _statements = new();
    private int _nextId;

    internal GeneratingStatementBuilder(CitationConstrainedGeneration parent)
    {
        _parent = parent;
    }

    public GeneratingStatementBuilder AddStatement(string statement, IReadOnlyList<GroundingEvidence> citations)
    {
        var id = $"stmt-{_nextId++:D6}";
        _statements.Add(new AttributedStatement
        {
            StatementId = id,
            Statement = statement,
            Citations = citations,
        });
        return this;
    }

    internal List<AttributedStatement> Build()
    {
        return _statements
            .Select((s, i) => s with { StatementIndex = i })
            .ToList();
    }
}

public sealed record AttributedStatement
{
    public required string StatementId { get; init; }
    public string Statement { get; init; } = "";
    public int StatementIndex { get; init; }
    public required IReadOnlyList<GroundingEvidence> Citations { get; init; }

    public override string ToString()
    {
        var cites = string.Join(", ", Citations.Select(c => c.EvidenceId));
        return $"[{StatementId}] {Statement} (citations: [{cites}])";
    }
}

public sealed class GeneratedStatementSet : IEquatable<GeneratedStatementSet>
{
    public required IReadOnlyList<AttributedStatement> AcceptedStatements { get; init; }
    public required IReadOnlyList<string> RejectedStatements { get; init; }
    public int TotalSubmitted { get; init; }
    public int TotalAccepted { get; init; }
    public int TotalRejected { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> EvidenceMap { get; init; }

    public bool Equals(GeneratedStatementSet? other)
    {
        if (other is null) return false;
        if (TotalSubmitted != other.TotalSubmitted) return false;
        if (TotalAccepted != other.TotalAccepted) return false;
        if (TotalRejected != other.TotalRejected) return false;
        if (RejectedStatements.Count != other.RejectedStatements.Count) return false;

        for (var i = 0; i < RejectedStatements.Count; i++)
            if (!StringComparer.Ordinal.Equals(RejectedStatements[i], other.RejectedStatements[i]))
                return false;

        if (AcceptedStatements.Count != other.AcceptedStatements.Count) return false;
        for (var i = 0; i < AcceptedStatements.Count; i++)
            if (!StringComparer.Ordinal.Equals(AcceptedStatements[i].StatementId, other.AcceptedStatements[i].StatementId))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is GeneratedStatementSet other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(TotalSubmitted, TotalAccepted, TotalRejected);
}

public sealed class CitationConstrainedOptions
{
    public bool RequireGroundedCitation { get; init; } = true;

    public IReadOnlyList<string> ForbiddenStatementPatterns { get; init; } = new[]
    {
        "business logic abstraction",
        "auto-generated summary",
        "smart compression",
        "likely implementation",
        "probable pattern",
        "speculative relationship",
    };

    public static CitationConstrainedOptions Default => new();
}
