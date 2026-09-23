namespace ShepherdAPI.Api.Models;

public class ScoreCategory
{
    public int ScoreCategoryID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool HigherIsBetter { get; set; }
}
