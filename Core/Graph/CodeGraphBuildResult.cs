using Core.Graph.Indexing;

namespace Core.Graph;

public sealed class CodeGraphBuildResult
{
    public required CodeGraph Graph { get; init; }

    public required GraphIndex Index { get; init; }
}
