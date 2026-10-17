using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Data;

/// <summary>
/// Data access for the recording aggregate: Execution and its descendants Artifact,
/// ScoreObservation, and ScoreObservationEvidence. Lock and mutability rules live in the
/// service layer — this repository only knows how to read and write rows.
/// </summary>
public interface IRecordingRepository
{
    // Execution
    Task<IReadOnlyList<Execution>> GetExecutionsByIterationAsync(long iterationId);
    Task<Execution?> GetExecutionAsync(long id);
    /// <summary>
    /// Creates the Execution through dbo.usp_CreateExecution, which validates the version lock and the
    /// phase's rule configuration and inserts in one transaction. Returns null when the iteration does not
    /// exist; a locked version or invalid rule configuration surfaces as SqlException 50100 / 50200.
    /// </summary>
    Task<long?> CreateExecutionAsync(Execution execution);

    /// <summary>Replaces the full set of run-facts that remain mutable until EndUTC is set (see docs/ARCHITECTURE.md §5.1).</summary>
    Task<bool> UpdateExecutionRunFactsAsync(long id, string? modelUsed, string? interfaceUsed, int interventionCount, string? outcomeNotes, DateTime? endUtc);

    // Artifact (insert-only, see docs/ARCHITECTURE.md §4.4)
    Task<IReadOnlyList<Artifact>> GetArtifactsByExecutionAsync(long executionId);
    Task<Artifact?> GetArtifactAsync(long id);
    Task<long> CreateArtifactAsync(Artifact artifact);

    // ScoreObservation
    Task<IReadOnlyList<ScoreObservation>> GetScoreObservationsByExecutionAsync(long executionId);
    Task<ScoreObservation?> GetScoreObservationAsync(long id);
    Task<long> CreateScoreObservationAsync(ScoreObservation observation);
    Task<bool> UpdateScoreObservationContentAsync(long id, decimal observedScore, DateTime? observedUtc, string? observationNotes);

    // ScoreObservationEvidence
    Task<IReadOnlyList<ScoreObservationEvidence>> GetEvidenceByScoreObservationAsync(long scoreObservationId);
    Task CreateEvidenceLinkAsync(ScoreObservationEvidence link);
    Task<bool> DeleteEvidenceLinkAsync(long scoreObservationId, long artifactId);
}
