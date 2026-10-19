using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Data;

/// <summary>
/// Data access for the experiment design aggregate: ExperimentVersion and its direct children
/// Iteration, Parameter, and ScoreRule. Design writes (ExperimentVersion fields, Iteration, Parameter and
/// ScoreRule create/update/delete) go through controlled-write procedures that take the parent-version lock
/// and enforce the locked and design-frozen rules; a null/false result means the target was not found.
/// </summary>
public interface IExperimentDesignRepository
{
    // ExperimentVersion
    Task<IReadOnlyList<ExperimentVersion>> GetExperimentVersionsAsync();
    Task<ExperimentVersion?> GetExperimentVersionAsync(long id);
    Task<long> CreateExperimentVersionAsync(string name, string versionLabel, int statusId, DateTime effectiveDateUtc, string description);
    Task<bool> UpdateExperimentVersionAsync(long id, string name, string versionLabel, DateTime effectiveDateUtc, string description);
    Task<bool> SetExperimentVersionStatusAsync(long id, int statusId);

    /// <summary>
    /// Runs dbo.usp_FinalizeExperimentVersion: snapshots every ScoreObservation and sets LockedUTC in one
    /// transaction. Returns the database-generated LockedUTC, or null if the version does not exist. Failed
    /// checks surface as the SqlExceptions the procedure throws (catalog errors 50100 and 50500-50507).
    /// </summary>
    Task<DateTime?> FinalizeExperimentVersionAsync(long id);

    // Iteration
    Task<IReadOnlyList<Iteration>> GetIterationsByExperimentVersionAsync(long experimentVersionId);
    Task<Iteration?> GetIterationAsync(long id);
    Task<long?> CreateIterationAsync(Iteration iteration);
    Task<bool> UpdateIterationAsync(Iteration iteration);
    Task<bool> DeleteIterationAsync(long id);

    // Parameter
    Task<IReadOnlyList<Parameter>> GetParametersByExperimentVersionAsync(long experimentVersionId);
    Task<Parameter?> GetParameterAsync(long id);
    Task<long?> CreateParameterAsync(Parameter parameter);
    Task<bool> UpdateParameterAsync(Parameter parameter);
    Task<bool> DeleteParameterAsync(long id);

    // ScoreRule (Issue #7) — create, update of the non-key fields, and physical delete, like Parameter.
    Task<IReadOnlyList<ScoreRule>> GetScoreRulesByExperimentVersionAsync(long experimentVersionId);
    Task<ScoreRule?> GetScoreRuleAsync(int id);
    Task<int?> CreateScoreRuleAsync(ScoreRule scoreRule);

    /// <summary>Runs dbo.usp_UpdateScoreRule: WeightPercent, MinScore, MaxScore, Active and RuleNotes only.</summary>
    Task<bool> UpdateScoreRuleAsync(ScoreRule scoreRule);

    /// <summary>Runs dbo.usp_DeleteScoreRule: a physical delete. A referenced rule is refused by its foreign keys.</summary>
    Task<bool> DeleteScoreRuleAsync(int id);
}
