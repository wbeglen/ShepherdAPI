namespace ShepherdAPI.Api.Models;

public class Rater
{
    public int RaterID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RaterType { get; set; }
    public int? AgentID { get; set; }
}
