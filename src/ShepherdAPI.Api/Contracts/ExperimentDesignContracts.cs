using System.ComponentModel.DataAnnotations;

namespace ShepherdAPI.Api.Contracts;

public class CreateExperimentVersionRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string VersionLabel { get; set; } = string.Empty;
    public int StatusID { get; set; }
    public DateTime EffectiveDateUTC { get; set; }
    [Required] public string Description { get; set; } = string.Empty;
}

/// <summary>Updates the descriptive fields only. StatusID and LockedUTC are never touched by this endpoint (see docs/ARCHITECTURE.md §5.3).</summary>
public class UpdateExperimentVersionRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string VersionLabel { get; set; } = string.Empty;
    public DateTime EffectiveDateUTC { get; set; }
    [Required] public string Description { get; set; } = string.Empty;
}

public class SetExperimentVersionStatusRequest
{
    public int StatusID { get; set; }
}

/// <summary>
/// Finalizes the version: snapshots its scores and sets LockedUTC in one transaction (Issue #2). Not reversible via the API.
/// StatusID is optional and ignored; use the status endpoint to change a version's StatusID.
/// </summary>
public class LockExperimentVersionRequest
{
    /// <summary>Accepted for compatibility with earlier clients and ignored by finalization.</summary>
    public int? StatusID { get; set; }
}

public class CreateIterationRequest
{
    public long ExperimentVersionID { get; set; }
    public int Sequence { get; set; }
    [Required, MaxLength(100)] public string IterationLabel { get; set; } = string.Empty;
    [Required] public string Prompt { get; set; } = string.Empty;
    [MaxLength(100)] public string? Scenario { get; set; }
    public string? Notes { get; set; }
}

public class UpdateIterationRequest
{
    public int Sequence { get; set; }
    [Required, MaxLength(100)] public string IterationLabel { get; set; } = string.Empty;
    [Required] public string Prompt { get; set; } = string.Empty;
    [MaxLength(100)] public string? Scenario { get; set; }
    public string? Notes { get; set; }
}

public class CreateParameterRequest
{
    public long ExperimentVersionID { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(400)] public string? Value { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    public string? Notes { get; set; }
}

public class UpdateParameterRequest
{
    [MaxLength(400)] public string? Value { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    public string? Notes { get; set; }
}

public class CreateScoreRuleRequest
{
    public long ExperimentVersionID { get; set; }
    public int PhaseID { get; set; }
    public int ScoreCategoryID { get; set; }
    [Range(0, 100)] public decimal WeightPercent { get; set; }
    public int MinScore { get; set; }
    public int MaxScore { get; set; }
    public bool Active { get; set; } = true;
    [MaxLength(300)] public string? RuleNotes { get; set; }
}

