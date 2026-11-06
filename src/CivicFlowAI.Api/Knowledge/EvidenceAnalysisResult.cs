namespace CivicFlowAI.Api.Knowledge;

public enum EvidenceStatus { SUPPORTED, NEEDS_CLARIFICATION, UNSUPPORTED }

public sealed record EvidenceAssessment(EvidenceStatus Status, IReadOnlyList<string> EvidenceIds,
    string Justification, string? ClarificationQuestion);

public enum EvidenceOutcome { Completed, EmptyIndex, InvalidQuestion, RetrievalFailed, Unavailable, InvalidResponse, Cancelled }

public sealed record EvidenceAnalysisResult(EvidenceOutcome Outcome, EvidenceAssessment? Assessment = null,
    IReadOnlyList<KnowledgeChunk>? Evidence = null, string? Error = null);
