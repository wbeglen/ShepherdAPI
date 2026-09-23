using System.ComponentModel.DataAnnotations;

namespace ShepherdAPI.Api.Contracts;

public class AgentRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string Provider { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string DefaultModel { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string DefaultInterface { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
}

public class ArtifactTypeRequest
{
    [Required, MaxLength(50)] public string Name { get; set; } = string.Empty;
    [MaxLength(300)] public string? Description { get; set; }
}

public class PhaseRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
}

public class RaterRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? RaterType { get; set; }
    public int? AgentID { get; set; }
}

public class ScoreCategoryRequest
{
    [Required, MaxLength(50)] public string Name { get; set; } = string.Empty;
    [MaxLength(200)] public string? Description { get; set; }
    public bool HigherIsBetter { get; set; } = true;
}

public class StatusRequest
{
    [Required, MaxLength(50)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? Description { get; set; }
}
