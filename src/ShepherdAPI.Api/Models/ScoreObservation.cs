namespace ShepherdAPI.Api.Models;

public class ScoreObservation
{
    public long ScoreObservationID { get; set; }
    public long ExecutionID { get; set; }
    public int ScoreRuleID { get; set; }
    public int RaterID { get; set; }
    public decimal ObservedScore { get; set; }
    public DateTime? ObservedUTC { get; set; }

    /// <summary>Legacy free-text evidence pointer, superseded by ScoreObservationEvidence/Artifact. Read-only via ShepherdAPI.</summary>
    public string? EvidenceURI { get; set; }
    public string? ObservationNotes { get; set; }
}
