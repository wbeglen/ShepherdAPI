namespace ShepherdAPI.Api.Models;

public class ExperimentVersion
{
    public long ExperimentVersionID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string VersionLabel { get; set; } = string.Empty;
    public int StatusID { get; set; }
    public DateTime EffectiveDateUTC { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Null = correctable. Set = the experimental record is finalized and immutable (see docs/ARCHITECTURE.md §5.3).</summary>
    public DateTime? LockedUTC { get; set; }
}
