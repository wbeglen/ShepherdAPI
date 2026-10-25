using System.Data;
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
            VALUES (@name, @versionLabel, @statusId, @effectiveDateUtc, @description, NULL);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """,
            new { name, versionLabel, statusId, effectiveDateUtc, description });
    }

    /// <summary>Runs dbo.usp_UpdateExperimentVersion: lock, locked/frozen checks, then the update. False if the version does not exist.</summary>
    public async Task<bool> UpdateExperimentVersionAsync(long id, string name, string versionLabel, DateTime effectiveDateUtc, string description)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", id);
        parameters.Add("@Name", name);
        parameters.Add("@VersionLabel", versionLabel);
        parameters.Add("@EffectiveDateUTC", effectiveDateUtc, DbType.DateTime2);
        parameters.Add("@Description", description);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_UpdateExperimentVersion", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Found");
    }

    public async Task<bool> SetExperimentVersionStatusAsync(long id, int statusId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "UPDATE dbo.ExperimentVersion SET StatusID = @statusId WHERE ExperimentVersionID = @id;",
            new { id, statusId });
        return rows > 0;
    }

    public async Task<DateTime?> FinalizeExperimentVersionAsync(long id, IReadOnlyList<(long ExecutionID, string Explanation)> unknownEnds)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", id);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
        if (unknownEnds.Count > 0)
        {
            // An omitted table-valued parameter is an empty table: the procedure's pre-V11 behavior.
            var table = new DataTable();
            table.Columns.Add("ExecutionID", typeof(long));
            table.Columns.Add("Explanation", typeof(string));
            foreach (var (executionId, explanation) in unknownEnds)
            {
                table.Rows.Add(executionId, explanation);
            }

            parameters.Add("@UnknownEnds", table.AsTableValuedParameter("dbo.FinalizationExceptionList"));
        }

        await connection.ExecuteAsync("dbo.usp_FinalizeExperimentVersion", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Found") ? parameters.Get<DateTime?>("@LockedUTC") : null;
    }

    public async Task<IReadOnlyList<FinalizationException>> GetFinalizationExceptionsAsync(long experimentVersionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<FinalizationException>(
            """
            SELECT ExperimentVersionID, ExecutionID, ExceptionCode, Explanation, RecordedUTC
            FROM dbo.FinalizationException WHERE ExperimentVersionID = @experimentVersionId ORDER BY ExecutionID, ExceptionCode;
            """,
            new { experimentVersionId });
        return rows.AsList();
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

    public async Task<long?> CreateIterationAsync(Iteration iteration)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", iteration.ExperimentVersionID);
        parameters.Add("@Sequence", iteration.Sequence);
        parameters.Add("@IterationLabel", iteration.IterationLabel);
        parameters.Add("@Prompt", iteration.Prompt);
        parameters.Add("@Scenario", iteration.Scenario);
        parameters.Add("@Notes", iteration.Notes);
        parameters.Add("@IterationID", dbType: DbType.Int64, direction: ParameterDirection.Output);
        parameters.Add("@VersionFound", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_CreateIteration", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@VersionFound") ? parameters.Get<long?>("@IterationID") : null;
    }

    public async Task<bool> UpdateIterationAsync(Iteration iteration)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@IterationID", iteration.IterationID);
        parameters.Add("@Sequence", iteration.Sequence);
        parameters.Add("@IterationLabel", iteration.IterationLabel);
        parameters.Add("@Prompt", iteration.Prompt);
        parameters.Add("@Scenario", iteration.Scenario);
        parameters.Add("@Notes", iteration.Notes);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_UpdateIteration", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Found");
    }

    public async Task<bool> DeleteIterationAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@IterationID", id);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_DeleteIteration", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Found");
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

    public async Task<long?> CreateParameterAsync(Parameter parameter)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", parameter.ExperimentVersionID);
        parameters.Add("@Name", parameter.Name);
        parameters.Add("@Value", parameter.Value);
        parameters.Add("@Unit", parameter.Unit);
        parameters.Add("@Notes", parameter.Notes);
        parameters.Add("@ParameterID", dbType: DbType.Int64, direction: ParameterDirection.Output);
        parameters.Add("@VersionFound", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_CreateParameter", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@VersionFound") ? parameters.Get<long?>("@ParameterID") : null;
    }

    public async Task<bool> UpdateParameterAsync(Parameter parameter)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ParameterID", parameter.ParameterID);
        parameters.Add("@Name", parameter.Name);
        parameters.Add("@Value", parameter.Value);
        parameters.Add("@Unit", parameter.Unit);
        parameters.Add("@Notes", parameter.Notes);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_UpdateParameter", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Found");
    }

    public async Task<bool> DeleteParameterAsync(long id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ParameterID", id);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_DeleteParameter", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Found");
    }

    // -- ScoreRule ----------------------------------------------------------------

    public async Task<IReadOnlyList<ScoreRule>> GetScoreRulesByExperimentVersionAsync(long experimentVersionId)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<ScoreRule>(
            """
            SELECT ScoreRuleID, ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active, RuleNotes
            FROM dbo.ScoreRule WHERE ExperimentVersionID = @experimentVersionId ORDER BY PhaseID, ScoreCategoryID;
            """,
            new { experimentVersionId });
        return rows.AsList();
    }

    public async Task<ScoreRule?> GetScoreRuleAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<ScoreRule>(
            """
            SELECT ScoreRuleID, ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active, RuleNotes
            FROM dbo.ScoreRule WHERE ScoreRuleID = @id;
            """,
            new { id });
    }

    public async Task<int?> CreateScoreRuleAsync(ScoreRule scoreRule)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", scoreRule.ExperimentVersionID);
        parameters.Add("@PhaseID", scoreRule.PhaseID);
        parameters.Add("@ScoreCategoryID", scoreRule.ScoreCategoryID);
        parameters.Add("@WeightPercent", scoreRule.WeightPercent, DbType.Decimal, precision: 5, scale: 2);
        parameters.Add("@MinScore", scoreRule.MinScore);
        parameters.Add("@MaxScore", scoreRule.MaxScore);
        parameters.Add("@Active", scoreRule.Active);
        parameters.Add("@RuleNotes", scoreRule.RuleNotes);
        parameters.Add("@ScoreRuleID", dbType: DbType.Int32, direction: ParameterDirection.Output);
        parameters.Add("@VersionFound", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_CreateScoreRule", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@VersionFound") ? parameters.Get<int?>("@ScoreRuleID") : null;
    }

    public async Task<bool> UpdateScoreRuleAsync(ScoreRule scoreRule)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ScoreRuleID", scoreRule.ScoreRuleID);
        parameters.Add("@WeightPercent", scoreRule.WeightPercent, DbType.Decimal, precision: 5, scale: 2);
        parameters.Add("@MinScore", scoreRule.MinScore);
        parameters.Add("@MaxScore", scoreRule.MaxScore);
        parameters.Add("@Active", scoreRule.Active);
        parameters.Add("@RuleNotes", scoreRule.RuleNotes);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_UpdateScoreRule", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Found");
    }

    public async Task<bool> DeleteScoreRuleAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var parameters = new DynamicParameters();
        parameters.Add("@ScoreRuleID", id);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_DeleteScoreRule", parameters, commandType: CommandType.StoredProcedure);
        return parameters.Get<bool>("@Found");
    }
}
