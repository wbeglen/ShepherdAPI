using Microsoft.AspNetCore.Mvc;

namespace ShepherdAPI.Api.Contracts;

/// <summary>
/// The 409 body of <c>POST /api/executions</c>. <see cref="Code"/> tells the cases apart
/// (<c>version-locked</c> or <c>invalid-rule-configuration</c>); the remaining members are present only for
/// <c>invalid-rule-configuration</c>. This type documents the contract for generated clients; the runtime
/// body is the ProblemDetails written by the exception middleware, which uses these same member names.
/// </summary>
public class ExecutionConflictProblem : ProblemDetails
{
    public string Code { get; set; } = string.Empty;
    public long? ExperimentVersionID { get; set; }
    public int? PhaseID { get; set; }

    /// <summary>The total the phase's active rules must reach (100.00).</summary>
    public decimal? ExpectedWeightPercent { get; set; }

    /// <summary>The total the phase's active rules actually reach (0 when there are none).</summary>
    public decimal? ActualWeightPercent { get; set; }

    /// <summary>The phase's active rules (RowState 'A') behind the total; empty when the phase has none.</summary>
    public List<ExecutionConflictRule>? ScoreRules { get; set; }
}

public record ExecutionConflictRule(int ScoreRuleID, int ScoreCategoryID, decimal WeightPercent, int MinScore, int MaxScore);
