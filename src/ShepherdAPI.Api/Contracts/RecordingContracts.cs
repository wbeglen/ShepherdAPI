using System.ComponentModel.DataAnnotations;

namespace ShepherdAPI.Api.Contracts;

public class CreateExecutionRequest
{
    public long IterationID { get; set; }
    public int AgentID { get; set; }
    public int PhaseID { get; set; }
    public int StatusID { get; set; }
    public DateTime StartUTC { get; set; }
    [MaxLength(200)] public string? ModelUsed { get; set; }
    [MaxLength(100)] public string? InterfaceUsed { get; set; }
    [Range(0, int.MaxValue)] public int InterventionCount { get; set; }
    public string? OutcomeNotes { get; set; }
    [Range(1, int.MaxValue)] public int AttemptNumber { get; set; } = 1;
}

/// <summary>
/// Replaces the full set of run-facts that remain mutable until EndUTC is set. Not a partial
/// PATCH — clients resend every field in this set, including ones they don't intend to change
/// (see docs/ARCHITECTURE.md §5.1). Identity fields (IterationID, AgentID, PhaseID, AttemptNumber,
/// StartUTC) are immutable from creation and are not part of this contract.
/// </summary>
public class UpdateExecutionRunFactsRequest
{
    [MaxLength(200)] public string? ModelUsed { get; set; }
    [MaxLength(100)] public string? InterfaceUsed { get; set; }
    [Range(0, int.MaxValue)] public int InterventionCount { get; set; }
    public string? OutcomeNotes { get; set; }
    public DateTime? EndUTC { get; set; }
}

public class CreateArtifactRequest
{
    public long ExecutionID { get; set; }
    public int ArtifactTypeID { get; set; }
    [Required, MaxLength(200)] public string OriginalName { get; set; } = string.Empty;
    [Required, MaxLength(2048)] public string OriginalURI { get; set; } = string.Empty;
    [Required, MaxLength(2048)] public string StoredURI { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string MimeType { get; set; } = string.Empty;
    [Range(0, long.MaxValue)] public long ByteLength { get; set; }
    [Required, MaxLength(64), MinLength(64)] public string SHA256 { get; set; } = string.Empty;
    public DateTime ReceivedUTC { get; set; }
    public DateTime? StoredUTC { get; set; }
}

public class CreateScoreObservationRequest
{
    public long ExecutionID { get; set; }
    public int ScoreRuleID { get; set; }
    public int RaterID { get; set; }
    public decimal ObservedScore { get; set; }
    public DateTime? ObservedUTC { get; set; }
    public string? ObservationNotes { get; set; }
}

/// <summary>The correction path for scoring/notes content while the governing ExperimentVersion is unlocked.</summary>
public class UpdateScoreObservationContentRequest
{
    public decimal ObservedScore { get; set; }
    public DateTime? ObservedUTC { get; set; }
    public string? ObservationNotes { get; set; }
}

public class CreateScoreObservationEvidenceRequest
{
    public long ArtifactID { get; set; }
    [MaxLength(500)] public string? EvidenceNotes { get; set; }
}
