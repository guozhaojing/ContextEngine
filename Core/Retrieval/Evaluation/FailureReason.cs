namespace Core.Retrieval.Evaluation;

public enum FailureReason
{
    None,
    VectorSimilarityLow,
    GraphExpansionInsufficient,
    RankingWeightMismatch,
    MissingChunkInSystem,
    SemanticDilution,
    BusinessSignalTooLow,
    ConfidenceTooLow,
    QueryTooGeneric,
    EntityEdgeMissing
}
