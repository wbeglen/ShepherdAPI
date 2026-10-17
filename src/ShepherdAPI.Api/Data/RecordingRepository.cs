using Dapper;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Data;

public class RecordingRepository(ISqlConnectionFactory connectionFactory) : IRecordingRepository
{
    // -- Execution --------------------------------------------------------------

    public async Task<IReadOnlyList<Execution>> GetExecutionsByIterationAsync(long iterationId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<Execution>(
            """
            SELECT ExecutionID, IterationID, AgentID, PhaseID, StatusID, StartUTC, EndUTC,
                   ModelUsed, InterfaceUsed, ArtifactURI, InterventionCount, OutcomeNotes, AttemptNumber
            FROM dbo.Execution WHERE IterationID = @iterationId ORDER BY AttemptNumber;
            """,
            new { iterationId });
        return rows.AsList();
    }

    public async Task<Execution?> GetExecutionAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Execution>(
            """
            SELECT ExecutionID, IterationID, AgentID, PhaseID, StatusID, StartUTC, EndUTC,
                   ModelUsed, InterfaceUsed, ArtifactURI, InterventionCount, OutcomeNotes, AttemptNumber
            FROM dbo.Execution WHERE ExecutionID = @id;
            """,
            new { id });
    }

    public async Task<long> CreateExecutionAsync(Execution execution)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<long>(
            """
            INSERT INTO dbo.Execution
                (IterationID, AgentID, PhaseID, StatusID, StartUTC, EndUTC, ModelUsed, InterfaceUsed,
                 InterventionCount, OutcomeNotes, AttemptNumber)
            VALUES
                (@IterationID, @AgentID, @PhaseID, @StatusID, @StartUTC, @EndUTC, @ModelUsed, @InterfaceUsed,
                 @InterventionCount, @OutcomeNotes, @AttemptNumber);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """,
            execution);
    }

    public async Task<bool> UpdateExecutionRunFactsAsync(long id, string? modelUsed, string? interfaceUsed, int interventionCount, string? outcomeNotes, DateTime? endUtc)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE dbo.Execution
            SET ModelUsed = @modelUsed, InterfaceUsed = @interfaceUsed, InterventionCount = @interventionCount,
                OutcomeNotes = @outcomeNotes, EndUTC = @endUtc
            WHERE ExecutionID = @id;
            """,
            new { id, modelUsed, interfaceUsed, interventionCount, outcomeNotes, endUtc });
        return rows > 0;
    }

    // -- Artifact -----------------------------------------------------------------

    public async Task<IReadOnlyList<Artifact>> GetArtifactsByExecutionAsync(long executionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<Artifact>(
            """
            SELECT ArtifactID, ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI,
                   MimeType, ByteLength, SHA256, ReceivedUTC, StoredUTC
            FROM dbo.Artifact WHERE ExecutionID = @executionId ORDER BY ReceivedUTC;
            """,
            new { executionId });
        return rows.AsList();
    }

    public async Task<Artifact?> GetArtifactAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Artifact>(
            """
            SELECT ArtifactID, ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI,
                   MimeType, ByteLength, SHA256, ReceivedUTC, StoredUTC
            FROM dbo.Artifact WHERE ArtifactID = @id;
            """,
            new { id });
    }

    public async Task<long> CreateArtifactAsync(Artifact artifact)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<long>(
            """
            INSERT INTO dbo.Artifact
                (ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI, MimeType, ByteLength, SHA256, ReceivedUTC, StoredUTC)
            VALUES
                (@ExecutionID, @ArtifactTypeID, @OriginalName, @OriginalURI, @StoredURI, @MimeType, @ByteLength, @SHA256, @ReceivedUTC, @StoredUTC);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """,
            artifact);
    }

    // -- ScoreObservation -----------------------------------------------------------

    public async Task<IReadOnlyList<ScoreObservation>> GetScoreObservationsByExecutionAsync(long executionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<ScoreObservation>(
            """
            SELECT ScoreObservationID, ExecutionID, ScoreRuleID, RaterID, ObservedScore, ObservedUTC,
                   EvidenceURI, ObservationNotes
            FROM dbo.ScoreObservation WHERE ExecutionID = @executionId ORDER BY ScoreRuleID, RaterID;
            """,
            new { executionId });
        return rows.AsList();
    }

    public async Task<ScoreObservation?> GetScoreObservationAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<ScoreObservation>(
            """
            SELECT ScoreObservationID, ExecutionID, ScoreRuleID, RaterID, ObservedScore, ObservedUTC,
                   EvidenceURI, ObservationNotes
            FROM dbo.ScoreObservation WHERE ScoreObservationID = @id;
            """,
            new { id });
    }

    public async Task<long> CreateScoreObservationAsync(ScoreObservation observation)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<long>(
            """
            INSERT INTO dbo.ScoreObservation (ExecutionID, ScoreRuleID, RaterID, ObservedScore, ObservedUTC, ObservationNotes)
            VALUES (@ExecutionID, @ScoreRuleID, @RaterID, @ObservedScore, @ObservedUTC, @ObservationNotes);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """,
            observation);
    }

    public async Task<bool> UpdateScoreObservationContentAsync(long id, decimal observedScore, DateTime? observedUtc, string? observationNotes)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE dbo.ScoreObservation
            SET ObservedScore = @observedScore, ObservedUTC = @observedUtc, ObservationNotes = @observationNotes
            WHERE ScoreObservationID = @id;
            """,
            new { id, observedScore, observedUtc, observationNotes });
        return rows > 0;
    }

    // -- ScoreObservationEvidence -----------------------------------------------------

    public async Task<IReadOnlyList<ScoreObservationEvidence>> GetEvidenceByScoreObservationAsync(long scoreObservationId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<ScoreObservationEvidence>(
            "SELECT ScoreObservationID, ArtifactID, EvidenceNotes FROM dbo.ScoreObservationEvidence WHERE ScoreObservationID = @scoreObservationId;",
            new { scoreObservationId });
        return rows.AsList();
    }

    public async Task CreateEvidenceLinkAsync(ScoreObservationEvidence link)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.ScoreObservationEvidence (ScoreObservationID, ArtifactID, EvidenceNotes) VALUES (@ScoreObservationID, @ArtifactID, @EvidenceNotes);",
            link);
    }

    public async Task<bool> DeleteEvidenceLinkAsync(long scoreObservationId, long artifactId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "DELETE FROM dbo.ScoreObservationEvidence WHERE ScoreObservationID = @scoreObservationId AND ArtifactID = @artifactId;",
            new { scoreObservationId, artifactId });
        return rows > 0;
    }
}
