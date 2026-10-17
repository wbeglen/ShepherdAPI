namespace ShepherdAPI.Api.Models;

public class ScoreRule
{
    public int ScoreRuleID { get; set; }
    public long ExperimentVersionID { get; set; }
    public int PhaseID { get; set; }
    public int ScoreCategoryID { get; set; }
    public decimal WeightPercent { get; set; }
    public int MinScore { get; set; }
    public int MaxScore { get; set; }
    public bool Active { get; set; }
    public string? RuleNotes { get; set; }

    /// <summary>'A' (active/selectable) or 'D' (deleted/retired — preserved for historical resolution). Set only via delete; create always produces 'A' via the database default.</summary>
    public string RowState { get; set; } = Models.RowState.Active.ToString();
}
