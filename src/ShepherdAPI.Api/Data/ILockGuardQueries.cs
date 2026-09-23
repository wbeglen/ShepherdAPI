namespace ShepherdAPI.Api.Data;

/// <summary>
/// Resolves the governing ExperimentVersion.LockedUTC for a row anywhere in its descendant
/// hierarchy (Iteration, Execution, ScoreObservation). This is the single query surface the
/// service layer uses to enforce the Alpha hard-lock invariant (docs/ARCHITECTURE.md §5.3) —
/// centralized so every write path checks the same thing the same way.
/// </summary>
public interface ILockGuardQueries
{
    Task<DateTime?> GetLockedUtcByExperimentVersionIdAsync(long experimentVersionId);
    Task<DateTime?> GetLockedUtcByIterationIdAsync(long iterationId);
    Task<DateTime?> GetLockedUtcByExecutionIdAsync(long executionId);
    Task<DateTime?> GetLockedUtcByScoreObservationIdAsync(long scoreObservationId);
}
