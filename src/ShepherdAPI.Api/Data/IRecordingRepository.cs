using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Data;

/// <summary>
/// Data access for the recording aggregate: Execution and its descendants Artifact,
/// ScoreObservation, and ScoreObservationEvidence. Every write goes through a controlled-write procedure that takes the
/// parent-version lock first and enforces the locked and run-facts rules itself; a null/false result means the target
/// was not found. The four run-facts, artifact and evidence writes retry once after a deadlock victim (1205).
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

    /// <summary>
    /// Replaces the full set of run-facts through dbo.usp_UpdateExecutionRunFacts, which rejects a locked version
    /// (SqlException 50100) and an Execution whose EndUTC is set (50201) under the lock. False when it does not exist.
    /// </summary>
    Task<bool> UpdateExecutionRunFactsAsync(long id, string? modelUsed, string? interfaceUsed, int interventionCount, string? outcomeNotes, DateTime? endUtc);

    // Artifact (insert-only, see docs/ARCHITECTURE.md §4.4)
    Task<IReadOnlyList<Artifact>> GetArtifactsByExecutionAsync(long executionId);
    Task<Artifact?> GetArtifactAsync(long id);
    /// <summary>Creates the Artifact through dbo.usp_CreateArtifact (SqlException 50100 for a locked version). Null when the Execution does not exist.</summary>
    Task<long?> CreateArtifactAsync(Artifact artifact);

    // ScoreObservation
    Task<IReadOnlyList<ScoreObservation>> GetScoreObservationsByExecutionAsync(long executionId);
    Task<ScoreObservation?> GetScoreObservationAsync(long id);
    /// <summary>
    /// Creates the observation through dbo.usp_CreateScoreObservation, which validates the version lock and the
    /// Execution/ScoreRule context and writes in one transaction. Returns null when the Execution does not exist;
    /// failures surface as SqlException 50100 / 50300 / 50302 (or 2627 for a duplicate).
    /// </summary>
    Task<long?> CreateScoreObservationAsync(ScoreObservation observation);

    /// <summary>
    /// Updates only the content columns through dbo.usp_UpdateScoreObservationContent (unlocked version, score in range).
    /// Returns false when the observation does not exist.
    /// </summary>
    Task<bool> UpdateScoreObservationContentAsync(long id, decimal observedScore, DateTime? observedUtc, string? observationNotes);

    // ScoreObservationEvidence
    Task<IReadOnlyList<ScoreObservationEvidence>> GetEvidenceByScoreObservationAsync(long scoreObservationId);
    /// <summary>Adds the link through dbo.usp_AddEvidence, locking both owning versions. False when the observation or artifact does not exist; 50100 for a locked version, 2627 for a duplicate.</summary>
    Task<bool> CreateEvidenceLinkAsync(ScoreObservationEvidence link);

    /// <summary>Removes the link through dbo.usp_RemoveEvidence. False when the link does not exist; 50100 for a locked version.</summary>
    Task<bool> DeleteEvidenceLinkAsync(long scoreObservationId, long artifactId);
}
