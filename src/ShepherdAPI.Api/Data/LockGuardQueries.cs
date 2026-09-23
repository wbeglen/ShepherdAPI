using Dapper;

namespace ShepherdAPI.Api.Data;

public class LockGuardQueries(ISqlConnectionFactory connectionFactory) : ILockGuardQueries
{
    public async Task<DateTime?> GetLockedUtcByExperimentVersionIdAsync(long experimentVersionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<DateTime?>(
            "SELECT LockedUTC FROM dbo.ExperimentVersion WHERE ExperimentVersionID = @experimentVersionId;",
            new { experimentVersionId });
    }

    public async Task<DateTime?> GetLockedUtcByIterationIdAsync(long iterationId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<DateTime?>(
            """
            SELECT ev.LockedUTC
            FROM dbo.Iteration i
            JOIN dbo.ExperimentVersion ev ON ev.ExperimentVersionID = i.ExperimentVersionID
            WHERE i.IterationID = @iterationId;
            """,
            new { iterationId });
    }

    public async Task<DateTime?> GetLockedUtcByExecutionIdAsync(long executionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<DateTime?>(
            """
            SELECT ev.LockedUTC
            FROM dbo.Execution e
            JOIN dbo.Iteration i ON i.IterationID = e.IterationID
            JOIN dbo.ExperimentVersion ev ON ev.ExperimentVersionID = i.ExperimentVersionID
            WHERE e.ExecutionID = @executionId;
            """,
            new { executionId });
    }

    public async Task<DateTime?> GetLockedUtcByScoreObservationIdAsync(long scoreObservationId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<DateTime?>(
            """
            SELECT ev.LockedUTC
            FROM dbo.ScoreObservation so
            JOIN dbo.Execution e ON e.ExecutionID = so.ExecutionID
            JOIN dbo.Iteration i ON i.IterationID = e.IterationID
            JOIN dbo.ExperimentVersion ev ON ev.ExperimentVersionID = i.ExperimentVersionID
            WHERE so.ScoreObservationID = @scoreObservationId;
            """,
            new { scoreObservationId });
    }
}
