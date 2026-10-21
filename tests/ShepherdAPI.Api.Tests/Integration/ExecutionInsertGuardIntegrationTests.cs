using System.Data;
using System.Text.Json;
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
/// Issue #1: creating an Execution requires the phase's ScoreRules to total exactly 100.00 in an
/// unlocked version. Real SQL Server, through the service and dbo.usp_CreateExecution.
/// </summary>
[Collection("Database")]
public class ExecutionInsertGuardIntegrationTests(DatabaseFixture fixture)
{
    private static int _sequence = 20000;

    private sealed record Rule(int Phase, decimal Weight, bool Active = true, bool Deleted = false);

    private ExperimentDesignService Design() => new(new ExperimentDesignRepository(fixture.ConnectionFactory));

    private RecordingService Recording(ISqlConnectionFactory? factory = null)
    {
        var f = factory ?? fixture.ConnectionFactory;
        return new RecordingService(new RecordingRepository(f), new ExperimentDesignRepository(f), new LockGuardQueries(f));
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private async Task ExecAsync(string sql)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync(sql);
    }

    private Task<int> NewPhaseAsync() => ScalarAsync<int>(
        "INSERT dbo.Phase (Name, Sequence) OUTPUT INSERTED.PhaseID VALUES (@n, @s);",
        new { n = "GuardPhase" + Guid.NewGuid().ToString("N"), s = Interlocked.Increment(ref _sequence) });

    private Task<int> NewCategoryAsync() => ScalarAsync<int>(
        "INSERT dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES (@n, 1);",
        new { n = "GuardCat" + Guid.NewGuid().ToString("N") });

    /// <summary>A fresh version with one iteration and the given rules (retired ones soft-deleted before any Execution exists).</summary>
    private async Task<(long Version, long Iteration, List<int> RuleIds)> SetupAsync(params Rule[] rules)
    {
        var design = Design();
        var version = await design.CreateExperimentVersionAsync(new CreateExperimentVersionRequest
        {
            Name = $"ExecGuard-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
            EffectiveDateUTC = DateTime.UtcNow, Description = "execution insert guard test",
        });
        var ruleIds = new List<int>();
        foreach (var rule in rules)
        {
            var created = await design.CreateScoreRuleAsync(new CreateScoreRuleRequest
            {
                ExperimentVersionID = version.ExperimentVersionID, PhaseID = rule.Phase, ScoreCategoryID = await NewCategoryAsync(),
                WeightPercent = rule.Weight, MinScore = 0, MaxScore = 5, Active = rule.Active,
            });
            ruleIds.Add(created.ScoreRuleID);
            if (rule.Deleted)
            {
                await design.DeleteScoreRuleAsync(created.ScoreRuleID);
            }
        }
        var iteration = await design.CreateIterationAsync(new CreateIterationRequest
        {
            ExperimentVersionID = version.ExperimentVersionID, Sequence = 1, IterationLabel = "i1", Prompt = "p",
        });
        return (version.ExperimentVersionID, iteration.IterationID, ruleIds);
    }

    private CreateExecutionRequest Request(long iterationId, int phaseId, int attempt = 1) => new()
    {
        IterationID = iterationId, AgentID = fixture.AgentId, PhaseID = phaseId, StatusID = fixture.DraftStatusId,
        StartUTC = DateTime.UtcNow, InterventionCount = 0, AttemptNumber = attempt,
    };

    private Task<int> ExecutionCountAsync(long iterationId) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Execution WHERE IterationID = @iterationId;", new { iterationId });

    private async Task AssertInvalidConfigurationAsync(Func<Task> create, decimal expectedActual, int expectedRuleCount)
    {
        var ex = await Assert.ThrowsAsync<CatalogException>(create);

        Assert.Same(ErrorCatalog.InvalidRuleConfiguration, ex.Entry);
        Assert.Equal(100.00m, ex.Extensions["expectedWeightPercent"]);
        Assert.Equal(expectedActual, ex.Extensions["actualWeightPercent"]);
        Assert.Equal(expectedRuleCount, ((List<ExecutionConflictRule>)ex.Extensions["scoreRules"]!).Count);
    }

    // ---------------------------------------------------------------- the weight rule

    [Theory]
    [InlineData("100")]
    [InlineData("60,40")]
    [InlineData("50,30,20")]
    [InlineData("99.99,0.01")]
    public async Task UsableRulesTotalling100_PermitInsertion_IntoAnUnlockedVersion(string weights)
    {
        var rules = weights.Split(',').Select(w => new Rule(fixture.PhaseId, decimal.Parse(w, System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var (_, iteration, _) = await SetupAsync(rules);

        var created = await Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId));

        Assert.Equal(iteration, created.IterationID);
        Assert.Equal(1, await ExecutionCountAsync(iteration));
    }

    [Theory]
    [InlineData("", "0")]
    [InlineData("90", "90")]
    [InlineData("60,50", "110")]
    [InlineData("99.99", "99.99")]
    [InlineData("60,40.01", "100.01")]
    public async Task EmptyUnderweightAndOverweightRuleSets_RejectInsertion_WithoutCreatingAnExecution(string weights, string expectedActual)
    {
        var rules = weights.Length == 0
            ? Array.Empty<Rule>()
            : weights.Split(',').Select(w => new Rule(fixture.PhaseId, decimal.Parse(w, System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var (_, iteration, _) = await SetupAsync(rules);

        await AssertInvalidConfigurationAsync(
            () => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)),
            decimal.Parse(expectedActual, System.Globalization.CultureInfo.InvariantCulture), rules.Length);

        Assert.Equal(0, await ExecutionCountAsync(iteration));
    }

    [Fact]
    public async Task DeletedRules_DoNotContributeToTheTotal()
    {
        // 100 kept + 50 deleted: the deleted rule no longer exists, so the total is exactly 100.
        var (_, permittedIteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 100m), new Rule(fixture.PhaseId, 50m, Deleted: true));
        await Recording().CreateExecutionAsync(Request(permittedIteration, fixture.PhaseId));

        // 50 kept + 50 deleted: a deleted rule cannot help reach 100.
        var (_, rejectedIteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 50m), new Rule(fixture.PhaseId, 50m, Deleted: true));
        await AssertInvalidConfigurationAsync(
            () => Recording().CreateExecutionAsync(Request(rejectedIteration, fixture.PhaseId)), 50m, 1);
        Assert.Equal(0, await ExecutionCountAsync(rejectedIteration));
    }

    [Fact]
    public async Task AnUpdatedRule_CountsWithItsCurrentWeight()
    {
        var (_, iteration, ruleIds) = await SetupAsync(new Rule(fixture.PhaseId, 60m), new Rule(fixture.PhaseId, 40m));
        await Design().UpdateScoreRuleAsync(ruleIds[1], new UpdateScoreRuleRequest { WeightPercent = 30m, MinScore = 0, MaxScore = 5 });

        await AssertInvalidConfigurationAsync(
            () => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)), 90m, 2);

        await Design().UpdateScoreRuleAsync(ruleIds[1], new UpdateScoreRuleRequest { WeightPercent = 40m, MinScore = 0, MaxScore = 5 });
        await Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId));
    }

    [Fact]
    public async Task TheActiveFlag_NeverAffectsEligibility()
    {
        // Inactive rules still count: 70 active + 30 inactive = 100 permits insertion.
        var (_, permittedIteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 70m, Active: true), new Rule(fixture.PhaseId, 30m, Active: false));
        await Recording().CreateExecutionAsync(Request(permittedIteration, fixture.PhaseId));

        // ...and an inactive rule alone totalling 100 is enough.
        var (_, onlyInactive, _) = await SetupAsync(new Rule(fixture.PhaseId, 100m, Active: false));
        await Recording().CreateExecutionAsync(Request(onlyInactive, fixture.PhaseId));

        // The flag does not rescue a bad total either: 40 active + 50 inactive = 90.
        var (_, rejectedIteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 40m, Active: true), new Rule(fixture.PhaseId, 50m, Active: false));
        await AssertInvalidConfigurationAsync(
            () => Recording().CreateExecutionAsync(Request(rejectedIteration, fixture.PhaseId)), 90m, 2);
    }

    [Fact]
    public async Task RulesOfOtherVersionsOrPhases_DoNotContribute()
    {
        var phase2 = await NewPhaseAsync();
        // Phase 1 has 60, phase 2 has 40 (together 100, but they are different phases).
        var (version1, iteration1, _) = await SetupAsync(new Rule(fixture.PhaseId, 60m), new Rule(phase2, 40m));
        // Another version with a phase-1 rule that must not leak in.
        await SetupAsync(new Rule(fixture.PhaseId, 40m));

        await AssertInvalidConfigurationAsync(
            () => Recording().CreateExecutionAsync(Request(iteration1, fixture.PhaseId)), 60m, 1);
        await AssertInvalidConfigurationAsync(
            () => Recording().CreateExecutionAsync(Request(iteration1, phase2)), 40m, 1);

        // Completing phase 1 permits phase 1 only.
        await Design().CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = version1, PhaseID = fixture.PhaseId, ScoreCategoryID = await NewCategoryAsync(),
            WeightPercent = 40m, MinScore = 0, MaxScore = 5, Active = true,
        });
        await Recording().CreateExecutionAsync(Request(iteration1, fixture.PhaseId));
        await AssertInvalidConfigurationAsync(
            () => Recording().CreateExecutionAsync(Request(iteration1, phase2, attempt: 2)), 40m, 1);
        Assert.Equal(1, await ExecutionCountAsync(iteration1));
    }

    // ---------------------------------------------------------------- order of checks

    [Fact]
    public async Task LockedVersion_RejectsInsertion_EvenWithAValidConfiguration_AndCreatesNothing()
    {
        var (version, iteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 100m));
        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, version);

        var ex = await Assert.ThrowsAsync<SqlException>(() => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)));

        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, ex.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal("version-locked", body.GetProperty("code").GetString());
        Assert.Equal(0, await ExecutionCountAsync(iteration));
    }

    [Fact]
    public async Task LockedVersionWithAnInvalidConfiguration_ReportsTheLockFirst()
    {
        var (version, iteration, _) = await SetupAsync(); // no rules at all
        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, version);

        var ex = await Assert.ThrowsAsync<SqlException>(() => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)));

        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, ex.Number);
    }

    [Fact]
    public async Task UnknownIteration_IsNotFound_AndNothingIsCreated()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Recording().CreateExecutionAsync(Request(long.MaxValue, fixture.PhaseId)));

        // The procedure itself reports a missing iteration without throwing, so the API can keep its 404.
        var id = await new RecordingRepository(fixture.ConnectionFactory).CreateExecutionAsync(new Models.Execution
        {
            IterationID = long.MaxValue, AgentID = fixture.AgentId, PhaseID = fixture.PhaseId, StatusID = fixture.DraftStatusId,
            StartUTC = DateTime.UtcNow, AttemptNumber = 1,
        });
        Assert.Null(id);
    }

    // ---------------------------------------------------------------- error bodies

    [Fact]
    public async Task InvalidConfigurationBody_ListsThePhaseRules_AndMatchesTheDocumentedContract()
    {
        var (version, iteration, ruleIds) = await SetupAsync(new Rule(fixture.PhaseId, 60m), new Rule(fixture.PhaseId, 50m), new Rule(fixture.PhaseId, 25m, Deleted: true));
        var thrown = await Assert.ThrowsAsync<CatalogException>(() => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)));

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        var problem = JsonSerializer.Deserialize<ExecutionConflictProblem>(body.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(409, status);
        Assert.Equal(409, problem.Status);
        Assert.Equal("invalid-rule-configuration", problem.Code);
        Assert.Equal(version, problem.ExperimentVersionID);
        Assert.Equal(fixture.PhaseId, problem.PhaseID);
        Assert.Equal(100.00m, problem.ExpectedWeightPercent);
        Assert.Equal(110.00m, problem.ActualWeightPercent);
        Assert.Equal([ruleIds[0], ruleIds[1]], problem.ScoreRules!.Select(r => r.ScoreRuleID).Order()); // the deleted rule is not listed
        Assert.Equal([60m, 50m], problem.ScoreRules!.OrderBy(r => r.ScoreRuleID).Select(r => r.WeightPercent));
    }

    [Fact]
    public async Task InvalidConfigurationBody_ForAPhaseWithNoRules_HasAnEmptyList_AndAZeroTotal()
    {
        var (_, iteration, _) = await SetupAsync();
        var thrown = await Assert.ThrowsAsync<CatalogException>(() => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)));

        var (_, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        var problem = JsonSerializer.Deserialize<ExecutionConflictProblem>(body.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(0m, problem.ActualWeightPercent);
        Assert.Empty(problem.ScoreRules!);
    }

    // ---------------------------------------------------------------- concurrency

    private static async Task AcquireAsync(IDbConnection connection, IDbTransaction transaction, long versionId, bool exclusive)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", versionId);
        parameters.Add("@Exclusive", exclusive);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_AcquireVersionLock", parameters, transaction, commandType: CommandType.StoredProcedure);
    }

    [Fact]
    public async Task ConcurrentRuleChange_IsSerialized_SoTheGuardSeesTheCommittedConfiguration()
    {
        // The phase starts at 60, which would be refused...
        var (version, iteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 60m));

        // ...but a concurrent controlled writer is adding the missing 40 (still uncommitted).
        using var writer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var writerTransaction = writer.BeginTransaction();
        await AcquireAsync(writer, writerTransaction, version, exclusive: false);
        await writer.ExecuteAsync(
            $"INSERT dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active) VALUES ({version}, {fixture.PhaseId}, {await NewCategoryAsync()}, 40, 0, 5, 1);",
            transaction: writerTransaction);

        var creation = Task.Run(() => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)));
        await Task.Delay(500);
        Assert.False(creation.IsCompleted, "the guard must wait for the rule change's lock");

        writerTransaction.Commit();

        // The guard now sees 60 + 40 and admits the Execution.
        var created = await creation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(iteration, created.IterationID);
    }

    [Fact]
    public async Task ConcurrentRuleChange_ThatBreaksTheTotal_IsSeenByTheGuard()
    {
        var (version, iteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 100m));

        using var writer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var writerTransaction = writer.BeginTransaction();
        await AcquireAsync(writer, writerTransaction, version, exclusive: false);
        await writer.ExecuteAsync(
            $"INSERT dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active) VALUES ({version}, {fixture.PhaseId}, {await NewCategoryAsync()}, 50, 0, 5, 1);",
            transaction: writerTransaction);

        var creation = Task.Run(() => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)));
        await Task.Delay(500);
        Assert.False(creation.IsCompleted);

        writerTransaction.Commit();

        var rejected = await Assert.ThrowsAsync<CatalogException>(() => creation.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(150m, rejected.Extensions["actualWeightPercent"]);
        Assert.Equal(0, await ExecutionCountAsync(iteration));
    }

    [Fact]
    public async Task ConcurrentFinalization_CannotAdmitAnExecution_AfterTheVersionIsLocked()
    {
        var (version, iteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 100m));

        using var finalizer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var finalizerTransaction = finalizer.BeginTransaction();
        await AcquireAsync(finalizer, finalizerTransaction, version, exclusive: true);

        var creation = Task.Run(() => Recording().CreateExecutionAsync(Request(iteration, fixture.PhaseId)));
        await Task.Delay(500);
        Assert.False(creation.IsCompleted, "creation must wait behind the finalization's exclusive lock");

        await LockedUtcTestHelper.LockAsync(finalizer, version, finalizerTransaction);
        finalizerTransaction.Commit();

        var rejected = await Assert.ThrowsAsync<SqlException>(() => creation.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, rejected.Number);
        Assert.Equal(0, await ExecutionCountAsync(iteration));
    }

    [Fact]
    public async Task CreationBlockedLongerThanTheLockTimeout_FailsRetryably_AndCreatesNothing()
    {
        var (version, iteration, _) = await SetupAsync(new Rule(fixture.PhaseId, 100m));

        using var holder = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var holderTransaction = holder.BeginTransaction();
        await AcquireAsync(holder, holderTransaction, version, exclusive: true);

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            Recording(fixture.CreateConnectionFactory(300)).CreateExecutionAsync(Request(iteration, fixture.PhaseId)));
        holderTransaction.Rollback();

        Assert.Equal(ErrorCatalog.LockTimeout, ex.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal("retryable-conflict", body.GetProperty("code").GetString());
        Assert.Equal(0, await ExecutionCountAsync(iteration));
    }
}
