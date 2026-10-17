using Dapper;
using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Services;
using ShepherdAPI.Api.Tests.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Real SQL Server errors, raised by the engine or by T-SQL THROW, pushed through the exception
/// middleware to confirm the status and code the catalog promises.
/// </summary>
[Collection("Database")]
public class DbErrorMappingIntegrationTests(DatabaseFixture fixture)
{
    private async Task<SqlException> CaptureAsync(string sql, ISqlConnectionFactory? factory = null)
    {
        using var connection = await (factory ?? fixture.ConnectionFactory).CreateOpenConnectionAsync();
        return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
    }

    [Fact]
    public async Task EveryThrowNumberInTheCatalog_ComesBackWithItsStatusAndCode()
    {
        foreach (var entry in ErrorCatalog.All.Where(e => e.SqlNumber is not null))
        {
            var thrown = await CaptureAsync($"THROW {entry.SqlNumber}, 'raised by test', 1;");
            Assert.Equal(entry.SqlNumber, thrown.Number);

            var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);

            Assert.Equal((int)entry.Status, status);
            Assert.Equal(entry.Code, body.GetProperty("code").GetString());
            Assert.Equal("raised by test", body.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task UnregisteredThrowNumber_IsAnInternalError()
    {
        var thrown = await CaptureAsync("THROW 59999, 'not in the catalog', 1;");

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);

        Assert.Equal(500, status);
        Assert.Equal("internal-error", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LockWaitTimeout_Is1222_AndMapsToTheRetryableConflict()
    {
        var holderStatusId = await InsertStatusAsync("LockHolder");

        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = holder.BeginTransaction();
        await holder.ExecuteAsync("UPDATE dbo.Status SET Description = 'held' WHERE StatusID = @id;", new { id = holderStatusId }, transaction);

        // A factory with a 200 ms LOCK_TIMEOUT: the blocked writer fails fast with 1222.
        var thrown = await CaptureAsync($"UPDATE dbo.Status SET Description = 'blocked' WHERE StatusID = {holderStatusId};",
            fixture.CreateConnectionFactory(200));
        transaction.Rollback();

        Assert.Equal(1222, thrown.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        Assert.Equal(409, status);
        Assert.Equal("retryable-conflict", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task DeadlockVictim_Is1205_AndMapsToTheSameRetryableConflict()
    {
        var statusA = await InsertStatusAsync("DeadlockA");
        var statusB = await InsertStatusAsync("DeadlockB");

        await using var first = new SqlConnection(fixture.ConnectionString);
        await using var second = new SqlConnection(fixture.ConnectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var firstTx = first.BeginTransaction();
        await using var secondTx = second.BeginTransaction();

        const string update = "UPDATE dbo.Status SET Description = 'x' WHERE StatusID = @id;";
        await first.ExecuteAsync(update, new { id = statusA }, firstTx);
        await second.ExecuteAsync(update, new { id = statusB }, secondTx);

        // Each now waits on the other's row. SQL Server picks one as the deadlock victim.
        var firstBlocked = TryExecuteAsync(first, update, statusB, firstTx);
        await Task.Delay(500);
        var secondBlocked = TryExecuteAsync(second, update, statusA, secondTx);
        var results = await Task.WhenAll(firstBlocked, secondBlocked);

        var victims = results.Where(r => r is not null).ToList();
        Assert.Single(victims);
        Assert.Equal(1205, victims[0]!.Number);

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(victims[0]!);
        Assert.Equal(409, status);
        Assert.Equal("retryable-conflict", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DuplicateObservation_IsRecognizedByItsConstraintName()
    {
        var (executionId, ruleId) = await CreateExecutionAndRuleAsync();
        var repository = new RecordingRepository(fixture.ConnectionFactory);
        var observation = new Models.ScoreObservation
        {
            ExecutionID = executionId, ScoreRuleID = ruleId, RaterID = fixture.RaterId, ObservedScore = 3,
        };
        await repository.CreateScoreObservationAsync(observation);

        var thrown = await Assert.ThrowsAsync<SqlException>(() => repository.CreateScoreObservationAsync(observation));

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        Assert.Equal(409, status);
        Assert.Equal("duplicate-observation", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnrelatedDuplicateKey_StaysTheGenericDuplicateKey()
    {
        var repository = new LookupRepository(fixture.ConnectionFactory);
        var agent = new Models.Agent { Name = "DupAgent", Provider = "p", DefaultModel = "m", DefaultInterface = "i", Active = true };
        await repository.CreateAgentAsync(agent);

        var thrown = await Assert.ThrowsAsync<SqlException>(() => repository.CreateAgentAsync(agent));

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        Assert.Equal(409, status);
        Assert.Equal("duplicate-key", body.GetProperty("code").GetString());
        Assert.Equal("A record with the same unique key already exists.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ForeignKeyViolation_StaysTheGenericConstraintViolation()
    {
        var repository = new RecordingRepository(fixture.ConnectionFactory);
        var orphan = new Models.Artifact
        {
            ExecutionID = long.MaxValue, ArtifactTypeID = fixture.ArtifactTypeId, OriginalName = "n", OriginalURI = "u", StoredURI = "u",
            MimeType = "m", ByteLength = 1, SHA256 = new string('A', 64), ReceivedUTC = DateTime.UtcNow,
        };

        var thrown = await Assert.ThrowsAsync<SqlException>(() => repository.CreateArtifactAsync(orphan));

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        Assert.Equal(409, status);
        Assert.Equal("constraint-violation", body.GetProperty("code").GetString());
    }

    private static async Task<SqlException?> TryExecuteAsync(SqlConnection connection, string sql, int id, SqlTransaction transaction)
    {
        try
        {
            await connection.ExecuteAsync(sql, new { id }, transaction);
            return null;
        }
        catch (SqlException ex)
        {
            return ex;
        }
    }

    private async Task<int> InsertStatusAsync(string name)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<int>(
            "INSERT INTO dbo.Status (Name) OUTPUT INSERTED.StatusID VALUES (@name);", new { name = name + Guid.NewGuid().ToString("N")[..8] });
    }

    private async Task<(long ExecutionId, int RuleId)> CreateExecutionAndRuleAsync()
    {
        var designService = new ExperimentDesignService(new ExperimentDesignRepository(fixture.ConnectionFactory));
        var recordingService = new RecordingService(
            new RecordingRepository(fixture.ConnectionFactory),
            new ExperimentDesignRepository(fixture.ConnectionFactory),
            new LockGuardQueries(fixture.ConnectionFactory));

        var version = await designService.CreateExperimentVersionAsync(new CreateExperimentVersionRequest
        {
            Name = $"ErrorMap-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
            EffectiveDateUTC = DateTime.UtcNow, Description = "error mapping test",
        });
        var rule = await designService.CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = version.ExperimentVersionID, PhaseID = fixture.PhaseId, ScoreCategoryID = fixture.ScoreCategoryId,
            WeightPercent = 100m, MinScore = 0, MaxScore = 5, Active = true,
        });
        var iteration = await designService.CreateIterationAsync(new CreateIterationRequest
        {
            ExperimentVersionID = version.ExperimentVersionID, Sequence = 1, IterationLabel = "i1", Prompt = "p",
        });
        var execution = await recordingService.CreateExecutionAsync(new CreateExecutionRequest
        {
            IterationID = iteration.IterationID, AgentID = fixture.AgentId, PhaseID = fixture.PhaseId,
            StatusID = fixture.DraftStatusId, StartUTC = DateTime.UtcNow, AttemptNumber = 1,
        });
        return (execution.ExecutionID, rule.ScoreRuleID);
    }
}
