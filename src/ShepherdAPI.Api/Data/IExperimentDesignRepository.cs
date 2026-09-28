using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Data;

/// <summary>
/// Data access for the experiment design aggregate: ExperimentVersion and its direct children
/// Iteration, Parameter, and ScoreRule. Lock enforcement lives in the service layer, not here —
/// this repository only knows how to read and write rows.
/// </summary>
public interface IExperimentDesignRepository
{
    // ExperimentVersion
    Task<IReadOnlyList<ExperimentVersion>> GetExperimentVersionsAsync();
    Task<ExperimentVersion?> GetExperimentVersionAsync(long id);
    Task<long> CreateExperimentVersionAsync(string name, string versionLabel, int statusId, DateTime effectiveDateUtc, string description);
    Task<bool> UpdateExperimentVersionAsync(long id, string name, string versionLabel, DateTime effectiveDateUtc, string description);
    Task<bool> SetExperimentVersionStatusAsync(long id, int statusId);
    Task<bool> LockExperimentVersionAsync(long id, int statusId, DateTime lockedUtc);

    // Iteration
    Task<IReadOnlyList<Iteration>> GetIterationsByExperimentVersionAsync(long experimentVersionId);
    Task<Iteration?> GetIterationAsync(long id);
    Task<long> CreateIterationAsync(Iteration iteration);
    Task<bool> UpdateIterationAsync(Iteration iteration);
    Task<bool> DeleteIterationAsync(long id);

    // Parameter
    Task<IReadOnlyList<Parameter>> GetParametersByExperimentVersionAsync(long experimentVersionId);
    Task<Parameter?> GetParameterAsync(long id);
    Task<long> CreateParameterAsync(Parameter parameter);
    Task<bool> UpdateParameterAsync(Parameter parameter);
    Task<bool> DeleteParameterAsync(long id);

    // ScoreRule — add/delete-only lifecycle; definitions are immutable after creation (no update).
    /// <summary>Active (RowState = 'A') rules only — the normal selection list.</summary>
    Task<IReadOnlyList<ScoreRule>> GetScoreRulesByExperimentVersionAsync(long experimentVersionId);

    /// <summary>Resolves a rule regardless of RowState, for historical/internal resolution of existing references.</summary>
    Task<ScoreRule?> GetScoreRuleAsync(int id);

    /// <summary>Always creates an Active rule — RowState is left to the database default ('A').</summary>
    Task<int> CreateScoreRuleAsync(ScoreRule scoreRule);

    /// <summary>Soft-deletes: sets RowState = 'D'. Never physically removes the row. Idempotent — a no-op if already 'D'.</summary>
    Task<bool> DeleteScoreRuleAsync(int id);
}
