using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Services;

/// <summary>
/// Explains an invalid-rule-configuration refusal (Issue #1). The check itself lives in
/// dbo.usp_CreateExecution; this only describes the phase's rules so the client
/// can show what is wrong. ScoreRule.Active plays no part, and rules of other versions or phases are ignored.
/// </summary>
public static class ExecutionRuleConfiguration
{
    public const decimal RequiredTotal = 100.00m;

    /// <summary>The extension members written into the 409 body; their names match <see cref="ExecutionConflictProblem"/>.</summary>
    public static IReadOnlyDictionary<string, object?> Describe(long experimentVersionId, int phaseId, IEnumerable<ScoreRule> rules)
    {
        var phaseRules = rules
            .Where(r => r.ExperimentVersionID == experimentVersionId && r.PhaseID == phaseId)
            .OrderBy(r => r.ScoreCategoryID)
            .ToList();

        return new Dictionary<string, object?>
        {
            ["experimentVersionID"] = experimentVersionId,
            ["phaseID"] = phaseId,
            ["expectedWeightPercent"] = RequiredTotal,
            ["actualWeightPercent"] = phaseRules.Sum(r => r.WeightPercent),
            ["scoreRules"] = phaseRules
                .Select(r => new ExecutionConflictRule(r.ScoreRuleID, r.ScoreCategoryID, r.WeightPercent, r.MinScore, r.MaxScore))
                .ToList(),
        };
    }
}
