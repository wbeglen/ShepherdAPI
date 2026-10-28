using System.Data;
using Dapper;
using ShepherdAPI.Api.Infrastructure;
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

    public async Task<long?> CreateExecutionAsync(Execution execution)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@IterationID", execution.IterationID);
        parameters.Add("@AgentID", execution.AgentID);
        parameters.Add("@PhaseID", execution.PhaseID);
        parameters.Add("@StatusID", execution.StatusID);
        parameters.Add("@StartUTC", execution.StartUTC, DbType.DateTime2);
        parameters.Add("@EndUTC", execution.EndUTC, DbType.DateTime2);
        parameters.Add("@ModelUsed", execution.ModelUsed);
        parameters.Add("@InterfaceUsed", execution.InterfaceUsed);
        parameters.Add("@InterventionCount", execution.InterventionCount);
        parameters.Add("@OutcomeNotes", execution.OutcomeNotes);
        parameters.Add("@AttemptNumber", execution.AttemptNumber);
        parameters.Add("@ExecutionID", dbType: DbType.Int64, direction: ParameterDirection.Output);
        parameters.Add("@IterationFound", dbType: DbType.Boolean, direction: ParameterDirection.Output);

        await connection.ExecuteAsync("dbo.usp_CreateExecution", parameters, commandType: CommandType.StoredProcedure);

        return parameters.Get<bool>("@IterationFound") ? parameters.Get<long?>("@ExecutionID") : null;
    }

    public Task<bool> UpdateExecutionRunFactsAsync(long id, string? modelUsed, string? interfaceUsed, int interventionCount, string? outcomeNotes, DateTime? endUtc) =>
        DeadlockRetry.RunAsync(async () =>
        {
            using var connection = await connectionFactory.CreateOpenConnectionAsync();
            var parameters = new DynamicParameters();
            parameters.Add("@ExecutionID", id);
            parameters.Add("@ModelUsed", modelUsed);
            parameters.Add("@InterfaceUsed", interfaceUsed);
            parameters.Add("@InterventionCount", interventionCount);
            parameters.Add("@OutcomeNotes", outcomeNotes);
            parameters.Add("@EndUTC", endUtc, DbType.DateTime2);
            parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            await connection.ExecuteAsync("dbo.usp_UpdateExecutionRunFacts", parameters, commandType: CommandType.StoredProcedure);
            return parameters.Get<bool>("@Found");
        });

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

    public Task<long?> CreateArtifactAsync(Artifact artifact) =>
        DeadlockRetry.RunAsync(async () =>
        {
            using var connection = await connectionFactory.CreateOpenConnectionAsync();
            var parameters = new DynamicParameters();
            parameters.Add("@ExecutionID", artifact.ExecutionID);
            parameters.Add("@ArtifactTypeID", artifact.ArtifactTypeID);
            parameters.Add("@OriginalName", artifact.OriginalName);
            parameters.Add("@OriginalURI", artifact.OriginalURI);
            parameters.Add("@StoredURI", artifact.StoredURI);
            parameters.Add("@MimeType", artifact.MimeType);
            parameters.Add("@ByteLength", artifact.ByteLength);
            parameters.Add("@SHA256", artifact.SHA256, DbType.AnsiString, size: 64);
            parameters.Add("@ReceivedUTC", artifact.ReceivedUTC, DbType.DateTime2);
            parameters.Add("@StoredUTC", artifact.StoredUTC, DbType.DateTime2);
            parameters.Add("@ArtifactID", dbType: DbType.Int64, direction: ParameterDirection.Output);
            parameters.Add("@ExecutionFound", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            await connection.ExecuteAsync("dbo.usp_CreateArtifact", parameters, commandType: CommandType.StoredProcedure);
            return parameters.Get<bool>("@ExecutionFound") ? parameters.Get<long?>("@ArtifactID") : null;
        });

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

    public async Task<long?> CreateScoreObservationAsync(ScoreObservation observation)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@ExecutionID", observation.ExecutionID);
        parameters.Add("@ScoreRuleID", observation.ScoreRuleID);
        parameters.Add("@RaterID", observation.RaterID);
        parameters.Add("@ObservedScore", observation.ObservedScore);
        parameters.Add("@ObservedUTC", observation.ObservedUTC, DbType.DateTime2);
        parameters.Add("@ObservationNotes", observation.ObservationNotes);
        parameters.Add("@ScoreObservationID", dbType: DbType.Int64, direction: ParameterDirection.Output);
        parameters.Add("@ExecutionFound", dbType: DbType.Boolean, direction: ParameterDirection.Output);

        await connection.ExecuteAsync("dbo.usp_CreateScoreObservation", parameters, commandType: CommandType.StoredProcedure);

        return parameters.Get<bool>("@ExecutionFound") ? parameters.Get<long?>("@ScoreObservationID") : null;
    }

    public async Task<bool> UpdateScoreObservationContentAsync(long id, decimal observedScore, DateTime? observedUtc, string? observationNotes)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();

        var parameters = new DynamicParameters();
        parameters.Add("@ScoreObservationID", id);
        parameters.Add("@ObservedScore", observedScore);
        parameters.Add("@ObservedUTC", observedUtc, DbType.DateTime2);
        parameters.Add("@ObservationNotes", observationNotes);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);

        await connection.ExecuteAsync("dbo.usp_UpdateScoreObservationContent", parameters, commandType: CommandType.StoredProcedure);

        return parameters.Get<bool>("@Found");
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

    public Task<bool> CreateEvidenceLinkAsync(ScoreObservationEvidence link) =>
        DeadlockRetry.RunAsync(async () =>
        {
            using var connection = await connectionFactory.CreateOpenConnectionAsync();
            var parameters = new DynamicParameters();
            parameters.Add("@ScoreObservationID", link.ScoreObservationID);
            parameters.Add("@ArtifactID", link.ArtifactID);
            parameters.Add("@EvidenceNotes", link.EvidenceNotes);
            parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            await connection.ExecuteAsync("dbo.usp_AddEvidence", parameters, commandType: CommandType.StoredProcedure);
            return parameters.Get<bool>("@Found");
        });

    public Task<bool> DeleteEvidenceLinkAsync(long scoreObservationId, long artifactId) =>
        DeadlockRetry.RunAsync(async () =>
        {
            using var connection = await connectionFactory.CreateOpenConnectionAsync();
            var parameters = new DynamicParameters();
            parameters.Add("@ScoreObservationID", scoreObservationId);
            parameters.Add("@ArtifactID", artifactId);
            parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            await connection.ExecuteAsync("dbo.usp_RemoveEvidence", parameters, commandType: CommandType.StoredProcedure);
            return parameters.Get<bool>("@Found");
        });
}
