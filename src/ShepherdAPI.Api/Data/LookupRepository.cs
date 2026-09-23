using Dapper;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Data;

public class LookupRepository(ISqlConnectionFactory connectionFactory) : ILookupRepository
{
    // -- Agent --------------------------------------------------------------

    public async Task<IReadOnlyList<Agent>> GetAgentsAsync()
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<Agent>(
            "SELECT AgentID, Name, Provider, DefaultModel, DefaultInterface, Active FROM dbo.Agent ORDER BY Name;");
        return rows.AsList();
    }

    public async Task<Agent?> GetAgentAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Agent>(
            "SELECT AgentID, Name, Provider, DefaultModel, DefaultInterface, Active FROM dbo.Agent WHERE AgentID = @id;",
            new { id });
    }

    public async Task<int> CreateAgentAsync(Agent agent)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO dbo.Agent (Name, Provider, DefaultModel, DefaultInterface, Active)
            OUTPUT INSERTED.AgentID
            VALUES (@Name, @Provider, @DefaultModel, @DefaultInterface, @Active);
            """,
            agent);
    }

    public async Task<bool> UpdateAgentAsync(Agent agent)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE dbo.Agent
            SET Name = @Name, Provider = @Provider, DefaultModel = @DefaultModel,
                DefaultInterface = @DefaultInterface, Active = @Active
            WHERE AgentID = @AgentID;
            """,
            agent);
        return rows > 0;
    }

    // -- ArtifactType ---------------------------------------------------------

    public async Task<IReadOnlyList<ArtifactType>> GetArtifactTypesAsync()
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<ArtifactType>(
            "SELECT ArtifactTypeID, Name, Description FROM dbo.ArtifactType ORDER BY Name;");
        return rows.AsList();
    }

    public async Task<ArtifactType?> GetArtifactTypeAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<ArtifactType>(
            "SELECT ArtifactTypeID, Name, Description FROM dbo.ArtifactType WHERE ArtifactTypeID = @id;",
            new { id });
    }

    public async Task<int> CreateArtifactTypeAsync(ArtifactType artifactType)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO dbo.ArtifactType (Name, Description)
            OUTPUT INSERTED.ArtifactTypeID
            VALUES (@Name, @Description);
            """,
            artifactType);
    }

    public async Task<bool> UpdateArtifactTypeAsync(ArtifactType artifactType)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "UPDATE dbo.ArtifactType SET Name = @Name, Description = @Description WHERE ArtifactTypeID = @ArtifactTypeID;",
            artifactType);
        return rows > 0;
    }

    // -- Phase ----------------------------------------------------------------

    public async Task<IReadOnlyList<Phase>> GetPhasesAsync()
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<Phase>(
            "SELECT PhaseID, Name, Sequence, Description FROM dbo.Phase ORDER BY Sequence;");
        return rows.AsList();
    }

    public async Task<Phase?> GetPhaseAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Phase>(
            "SELECT PhaseID, Name, Sequence, Description FROM dbo.Phase WHERE PhaseID = @id;",
            new { id });
    }

    public async Task<int> CreatePhaseAsync(Phase phase)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO dbo.Phase (Name, Sequence, Description)
            OUTPUT INSERTED.PhaseID
            VALUES (@Name, @Sequence, @Description);
            """,
            phase);
    }

    public async Task<bool> UpdatePhaseAsync(Phase phase)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "UPDATE dbo.Phase SET Name = @Name, Sequence = @Sequence, Description = @Description WHERE PhaseID = @PhaseID;",
            phase);
        return rows > 0;
    }

    // -- Rater ------------------------------------------------------------------

    public async Task<IReadOnlyList<Rater>> GetRatersAsync()
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<Rater>(
            "SELECT RaterID, Name, RaterType, AgentID FROM dbo.Rater ORDER BY Name;");
        return rows.AsList();
    }

    public async Task<Rater?> GetRaterAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Rater>(
            "SELECT RaterID, Name, RaterType, AgentID FROM dbo.Rater WHERE RaterID = @id;",
            new { id });
    }

    public async Task<int> CreateRaterAsync(Rater rater)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO dbo.Rater (Name, RaterType, AgentID)
            OUTPUT INSERTED.RaterID
            VALUES (@Name, @RaterType, @AgentID);
            """,
            rater);
    }

    public async Task<bool> UpdateRaterAsync(Rater rater)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "UPDATE dbo.Rater SET Name = @Name, RaterType = @RaterType, AgentID = @AgentID WHERE RaterID = @RaterID;",
            rater);
        return rows > 0;
    }

    // -- ScoreCategory ------------------------------------------------------------

    public async Task<IReadOnlyList<ScoreCategory>> GetScoreCategoriesAsync()
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<ScoreCategory>(
            "SELECT ScoreCategoryID, Name, Description, HigherIsBetter FROM dbo.ScoreCategory ORDER BY Name;");
        return rows.AsList();
    }

    public async Task<ScoreCategory?> GetScoreCategoryAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<ScoreCategory>(
            "SELECT ScoreCategoryID, Name, Description, HigherIsBetter FROM dbo.ScoreCategory WHERE ScoreCategoryID = @id;",
            new { id });
    }

    public async Task<int> CreateScoreCategoryAsync(ScoreCategory category)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO dbo.ScoreCategory (Name, Description, HigherIsBetter)
            OUTPUT INSERTED.ScoreCategoryID
            VALUES (@Name, @Description, @HigherIsBetter);
            """,
            category);
    }

    public async Task<bool> UpdateScoreCategoryAsync(ScoreCategory category)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE dbo.ScoreCategory
            SET Name = @Name, Description = @Description, HigherIsBetter = @HigherIsBetter
            WHERE ScoreCategoryID = @ScoreCategoryID;
            """,
            category);
        return rows > 0;
    }

    // -- Status -----------------------------------------------------------------

    public async Task<IReadOnlyList<Status>> GetStatusesAsync()
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.QueryAsync<Status>(
            "SELECT StatusID, Name, Description FROM dbo.Status ORDER BY Name;");
        return rows.AsList();
    }

    public async Task<Status?> GetStatusAsync(int id)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<Status>(
            "SELECT StatusID, Name, Description FROM dbo.Status WHERE StatusID = @id;",
            new { id });
    }

    public async Task<int> CreateStatusAsync(Status status)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>(
            """
            INSERT INTO dbo.Status (Name, Description)
            OUTPUT INSERTED.StatusID
            VALUES (@Name, @Description);
            """,
            status);
    }

    public async Task<bool> UpdateStatusAsync(Status status)
    {
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        var rows = await connection.ExecuteAsync(
            "UPDATE dbo.Status SET Name = @Name, Description = @Description WHERE StatusID = @StatusID;",
            status);
        return rows > 0;
    }
}
