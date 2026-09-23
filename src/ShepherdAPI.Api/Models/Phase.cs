namespace ShepherdAPI.Api.Models;

public class Phase
{
    public int PhaseID { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string? Description { get; set; }
}
