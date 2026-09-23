namespace ShepherdAPI.Api.Models;

public class Execution
{
    public long ExecutionID { get; set; }
    public long IterationID { get; set; }
    public int AgentID { get; set; }
    public int PhaseID { get; set; }
    public int StatusID { get; set; }
    public DateTime StartUTC { get; set; }

    /// <summary>Set by the run itself finishing. See docs/ARCHITECTURE.md §5.1.</summary>
    public DateTime? EndUTC { get; set; }
    public string? ModelUsed { get; set; }
    public string? InterfaceUsed { get; set; }

    /// <summary>Legacy free-text evidence pointer, superseded by Artifact. Read-only via ShepherdAPI.</summary>
    public string? ArtifactURI { get; set; }
    public int InterventionCount { get; set; }
    public string? OutcomeNotes { get; set; }
    public int AttemptNumber { get; set; }
}
