namespace ShepherdAPI.Api.Models;

public class Iteration
{
    public long IterationID { get; set; }
    public long ExperimentVersionID { get; set; }
    public int Sequence { get; set; }
    public string IterationLabel { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? Scenario { get; set; }
    public string? Notes { get; set; }
}
