// =============================================================================
// Detection/MissingContextDetector.cs — detects gaps in context completeness
// =============================================================================

using Core.Context.Models;
using Core.Prompting.Models;

namespace Core.Prompting.Detection;

public sealed class MissingContextDetector
{
    public IReadOnlyList<MissingContextIssue> Detect(StructuredContext context)
    {
        var issues = new List<MissingContextIssue>();
        var issueCounter = 0;

        DetectMissingRepositoryImplementation(context, issues, ref issueCounter);
        DetectUnresolvedEntityMapping(context, issues, ref issueCounter);
        DetectLowConfidencePaths(context, issues, ref issueCounter);
        DetectDisconnectedSegments(context, issues, ref issueCounter);
        DetectDynamicBlindSpots(context, issues, ref issueCounter);
        DetectMissingRouteCoverage(context, issues, ref issueCounter);

        return issues;
    }

    private static void DetectMissingRepositoryImplementation(
        StructuredContext context,
        List<MissingContextIssue> issues,
        ref int counter)
    {
        if (context.Entities.Count > 0 && context.CompressedMethods.Count == 0)
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"missing-repo-{++counter}",
                Kind = MissingContextKind.MissingRepositoryImplementation,
                Description = $"Entities ({string.Join(", ", context.Entities.Take(3))}) were identified but no repository/service implementations were found in the retrieval results.",
                AffectedEntity = context.Entities.FirstOrDefault(),
                Severity = 0.8,
                Recommendation = "Verify that the scanned project contains concrete repository implementations for these entities. Check if repositories use generic base classes that need resolution."
            });
        }

        if (context.Tables.Count > 0 && context.CompressedMethods.All(m =>
            !m.Contains("Repository", StringComparison.Ordinal) &&
            !m.Contains("Session", StringComparison.Ordinal)))
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"missing-data-access-{++counter}",
                Kind = MissingContextKind.MissingRepositoryImplementation,
                Description = $"Table access detected for {context.Tables.Count} tables but no NHibernate session/repository methods found in context.",
                Severity = 0.7,
                Recommendation = "Check if data access methods exist in the scanned solution but were not retrieved. The project may use indirect access patterns (e.g., generic repositories, HQL queries)."
            });
        }
    }

    private static void DetectUnresolvedEntityMapping(
        StructuredContext context,
        List<MissingContextIssue> issues,
        ref int counter)
    {
        if (context.Entities.Count > 0 && context.Tables.Count == 0)
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"unresolved-mapping-{++counter}",
                Kind = MissingContextKind.UnresolvedEntityMapping,
                Description = $"Found {context.Entities.Count} entity classes but no corresponding table mappings. Entity-to-table resolution may be incomplete.",
                AffectedEntity = context.Entities.FirstOrDefault(),
                Severity = 0.75,
                Recommendation = "Verify that NHibernate .hbm.xml mappings or entity attributes exist for these classes. Check GenericInheritanceMap resolution confidence."
            });
        }

        if (context.Entities.Count < context.Tables.Count)
        {
            var unreferenced = context.Tables.Count - context.Entities.Count;
            issues.Add(new MissingContextIssue
            {
                IssueId = $"partial-mapping-{++counter}",
                Kind = MissingContextKind.UnresolvedEntityMapping,
                Description = $"{unreferenced} table(s) have no associated entity class in the context. Entity resolution may be incomplete.",
                Severity = 0.5,
                Recommendation = "These tables may be accessed through generic repositories where entity type is not statically determinable, or through dynamic HQL/SQL queries."
            });
        }
    }

    private static void DetectLowConfidencePaths(
        StructuredContext context,
        List<MissingContextIssue> issues,
        ref int counter)
    {
        var lowConfidencePaths = context.SemanticPaths
            .Where(p => p.Contains("Low", StringComparison.OrdinalIgnoreCase) ||
                        p.Contains("Medium", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (lowConfidencePaths.Count > 0)
        {
            var ratio = (double)lowConfidencePaths.Count / Math.Max(context.SemanticPaths.Count, 1);
            if (ratio > 0.3)
            {
                issues.Add(new MissingContextIssue
                {
                    IssueId = $"low-confidence-{++counter}",
                    Kind = MissingContextKind.LowConfidencePath,
                    Description = $"{lowConfidencePaths.Count}/{context.SemanticPaths.Count} semantic paths have low or medium confidence. Resolution accuracy may be reduced.",
                    Severity = Math.Min(0.9, 0.4 + ratio),
                    Recommendation = "Consider adding explicit entity type annotations or upgrading resolution strategies. Check if generic type parameters need manual resolution."
                });
            }
        }
    }

    private static void DetectDisconnectedSegments(
        StructuredContext context,
        List<MissingContextIssue> issues,
        ref int counter)
    {
        if (context.Routes.Count > 0 && context.SemanticPaths.Count == 0)
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"disconnected-routes-{++counter}",
                Kind = MissingContextKind.DisconnectedSegment,
                Description = "Routes were found but no semantic paths connect them to data access layers. The call graph may have gaps.",
                AffectedRoute = context.Routes.FirstOrDefault(),
                Severity = 0.7,
                Recommendation = "Check if the graph contains edges between controllers and services. Verify that all projects in the solution were scanned."
            });
        }

        if (context.Entities.Count > 0 && context.SemanticPaths.Count == 0)
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"disconnected-entities-{++counter}",
                Kind = MissingContextKind.DisconnectedSegment,
                Description = "Entities were found but no semantic paths connect them to caller methods. Data access edges may be missing.",
                Severity = 0.65,
                Recommendation = "Verify that nh:entity-access edges were produced by analyzers. Check if entities are accessed through generic layers."
            });
        }
    }

    private static void DetectDynamicBlindSpots(
        StructuredContext context,
        List<MissingContextIssue> issues,
        ref int counter)
    {
        var hasHqlOrSql = context.BusinessRules.Any(r =>
            r.Contains("HQL", StringComparison.OrdinalIgnoreCase) ||
            r.Contains("SQL", StringComparison.OrdinalIgnoreCase));

        if (hasHqlOrSql || context.Metadata.TryGetValue("has_dynamic_queries", out var dyn) && dyn == "true")
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"dynamic-blind-{++counter}",
                Kind = MissingContextKind.DynamicBlindSpot,
                Description = "Dynamic queries (HQL/SQL) detected. Static analysis cannot fully resolve runtime-generated query strings or reflection-based data access.",
                Severity = 0.6,
                Recommendation = "These query paths require runtime instrumentation or manual annotation for complete coverage."
            });
        }

        var hasReflection = context.CompressedMethods.Any(m =>
            m.Contains("Invoke(", StringComparison.Ordinal) ||
            m.Contains("Activator", StringComparison.Ordinal) ||
            m.Contains("Assembly.", StringComparison.Ordinal) ||
            m.Contains("GetType(", StringComparison.Ordinal));

        if (hasReflection)
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"reflection-blind-{++counter}",
                Kind = MissingContextKind.DynamicBlindSpot,
                Description = "Reflection-based calls detected. These cannot be statically analyzed and represent blind spots in the call graph.",
                Severity = 0.7,
                Recommendation = "Reflection-based method resolution requires runtime tracing. Consider annotating known reflection targets."
            });
        }
    }

    private static void DetectMissingRouteCoverage(
        StructuredContext context,
        List<MissingContextIssue> issues,
        ref int counter)
    {
        if (context.Entities.Count > 0 && context.Routes.Count == 0)
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"missing-routes-{++counter}",
                Kind = MissingContextKind.MissingRouteCoverage,
                Description = "Entities and data access were found but no API routes. The solution may not have ASP.NET controllers, or routes were not analyzed.",
                Severity = 0.6,
                Recommendation = "Verify AspNetRouteAnalyzer is active and the solution contains ASP.NET Controllers with [Route] or [Http*] attributes."
            });
        }

        if (context.Routes.Count > 0 && context.Routes.Count < 3 && context.Entities.Count > 5)
        {
            issues.Add(new MissingContextIssue
            {
                IssueId = $"low-route-coverage-{++counter}",
                Kind = MissingContextKind.MissingRouteCoverage,
                Description = $"Only {context.Routes.Count} route(s) found for {context.Entities.Count} entities. Route coverage may be incomplete.",
                Severity = 0.5,
                Recommendation = "Some entities may be accessed through internal services (not exposed via HTTP routes) or through background jobs."
            });
        }
    }
}
