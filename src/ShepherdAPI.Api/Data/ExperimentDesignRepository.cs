using Dapper;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Data;

public class ExperimentDesignRepository(ISqlConnectionFactory connectionFactory) : IExperimentDesignRepository
{
    // -- ExperimentVersion --------------------------------------------------

    public async Task<IReadOnlyList<ExperimentVersion>> GetExperimentVersionsAsync()
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<ExperimentVersion>(
            """
            SELECT ExperimentVersionID, Name, VersionLabel, StatusID, EffectiveDateUTC, Description, LockedUTC
            FROM dbo.ExperimentVersion ORDER BY Name, VersionLabel;
            """);
        return rows.AsList();
    }

    public async Task<ExperimentVersion?> GetExperimentVersionAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<ExperimentVersion>(
            """
            SELECT ExperimentVersionID, Name, VersionLabel, StatusID, EffectiveDateUTC, Description, LockedUTC
            FROM dbo.ExperimentVersion WHERE ExperimentVersionID = @id;
            """,
            new { id });
    }

    public async Task<long> CreateExperimentVersionAsync(string name, string versionLabel, int statusId, DateTime effectiveDateUtc, string description)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<long>(
            """
            INSERT INTO dbo.ExperimentVersion (Name, VersionLabel, StatusID, EffectiveDateUTC, Description, LockedUTC)
            OUTPUT INSERTED.ExperimentVersionID
            VALUES (@name, @versionLabel, @statusId, @effectiveDateUtc, @description, NULL);
            """,
            new { name, versionLabel, statusId, effectiveDateUtc, description });
    }

    public async Task<bool> UpdateExperimentVersionAsync(long id, string name, string versionLabel, DateTime effectiveDateUtc, string description)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE dbo.ExperimentVersion
            SET Name = @name, VersionLabel = @versionLabel, EffectiveDateUTC = @effectiveDateUtc, Description = @description
            WHERE ExperimentVersionID = @id;
            """,
            new { id, name, versionLabel, effectiveDateUtc, description });
        return rows > 0;
    }

    public async Task<bool> SetExperimentVersionStatusAsync(long id, int statusId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "UPDATE dbo.ExperimentVersion SET StatusID = @statusId WHERE ExperimentVersionID = @id;",
            new { id, statusId });
        return rows > 0;
    }

    public async Task<bool> LockExperimentVersionAsync(long id, int statusId, DateTime lockedUtc)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE dbo.ExperimentVersion
            SET StatusID = @statusId, LockedUTC = @lockedUtc
            WHERE ExperimentVersionID = @id AND LockedUTC IS NULL;
            """,
            new { id, statusId, lockedUtc });
        return rows > 0;
    }

    // -- Iteration ------------------------------------------------------------

    public async Task<IReadOnlyList<Iteration>> GetIterationsByExperimentVersionAsync(long experimentVersionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<Iteration>(
            """
            SELECT IterationID, ExperimentVersionID, Sequence, IterationLabel, Prompt, Scenario, Notes
            FROM dbo.Iteration WHERE ExperimentVersionID = @experimentVersionId ORDER BY Sequence;
            """,
            new { experimentVersionId });
        return rows.AsList();
    }

    public async Task<Iteration?> GetIterationAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Iteration>(
            """
            SELECT IterationID, ExperimentVersionID, Sequence, IterationLabel, Prompt, Scenario, Notes
            FROM dbo.Iteration WHERE IterationID = @id;
            """,
            new { id });
    }

    public async Task<long> CreateIterationAsync(Iteration iteration)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<long>(
            """
            INSERT INTO dbo.Iteration (ExperimentVersionID, Sequence, IterationLabel, Prompt, Scenario, Notes)
            OUTPUT INSERTED.IterationID
            VALUES (@ExperimentVersionID, @Sequence, @IterationLabel, @Prompt, @Scenario, @Notes);
            """,
            iteration);
    }

    public async Task<bool> UpdateIterationAsync(Iteration iteration)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE dbo.Iteration
            SET Sequence = @Sequence, IterationLabel = @IterationLabel, Prompt = @Prompt,
                Scenario = @Scenario, Notes = @Notes
            WHERE IterationID = @IterationID;
            """,
            iteration);
        return rows > 0;
    }

    public async Task<bool> DeleteIterationAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync("DELETE FROM dbo.Iteration WHERE IterationID = @id;", new { id });
        return rows > 0;
    }

    // -- Parameter --------------------------------------------------------------

    public async Task<IReadOnlyList<Parameter>> GetParametersByExperimentVersionAsync(long experimentVersionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<Parameter>(
            """
            SELECT ParameterID, ExperimentVersionID, Name, Value, Unit, Notes
            FROM dbo.Parameter WHERE ExperimentVersionID = @experimentVersionId ORDER BY Name;
            """,
            new { experimentVersionId });
        return rows.AsList();
    }

    public async Task<Parameter?> GetParameterAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Parameter>(
            "SELECT ParameterID, ExperimentVersionID, Name, Value, Unit, Notes FROM dbo.Parameter WHERE ParameterID = @id;",
            new { id });
    }

    public async Task<long> CreateParameterAsync(Parameter parameter)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<long>(
            """
            INSERT INTO dbo.Parameter (ExperimentVersionID, Name, Value, Unit, Notes)
            OUTPUT INSERTED.ParameterID
            VALUES (@ExperimentVersionID, @Name, @Value, @Unit, @Notes);
            """,
            parameter);
    }

    public async Task<bool> UpdateParameterAsync(Parameter parameter)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "UPDATE dbo.Parameter SET Name = @Name, Value = @Value, Unit = @Unit, Notes = @Notes WHERE ParameterID = @ParameterID;",
            parameter);
        return rows > 0;
    }

    public async Task<bool> DeleteParameterAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync("DELETE FROM dbo.Parameter WHERE ParameterID = @id;", new { id });
        return rows > 0;
    }

    // -- ScoreRule ----------------------------------------------------------------

    public async Task<IReadOnlyList<ScoreRule>> GetScoreRulesByExperimentVersionAsync(long experimentVersionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<ScoreRule>(
            """
            SELECT ScoreRuleID, ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active, RuleNotes, RowState
            FROM dbo.ScoreRule WHERE ExperimentVersionID = @experimentVersionId AND RowState = 'A' ORDER BY PhaseID, ScoreCategoryID;
            """,
            new { experimentVersionId });
        return rows.AsList();
    }

    public async Task<ScoreRule?> GetScoreRuleAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<ScoreRule>(
            """
            SELECT ScoreRuleID, ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active, RuleNotes, RowState
            FROM dbo.ScoreRule WHERE ScoreRuleID = @id;
            """,
            new { id });
    }

    public async Task<int> CreateScoreRuleAsync(ScoreRule scoreRule)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active, RuleNotes)
            OUTPUT INSERTED.ScoreRuleID
            VALUES (@ExperimentVersionID, @PhaseID, @ScoreCategoryID, @WeightPercent, @MinScore, @MaxScore, @Active, @RuleNotes);
            """,
            scoreRule);
    }

    public async Task<bool> DeleteScoreRuleAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "UPDATE dbo.ScoreRule SET RowState = 'D' WHERE ScoreRuleID = @id AND RowState = 'A';",
            new { id });
        return rows > 0;
    }
}
