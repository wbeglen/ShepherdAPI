using System.Data;
using System.Globalization;
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
/// Issue #3: ScoreObservation writes are validated against the Execution and ScoreRule context, atomically
/// with the write, through dbo.usp_CreateScoreObservation / usp_UpdateScoreObservationContent. Real SQL Server.
/// </summary>
[Collection("Database")]
public class ScoreObservationWriteValidationIntegrationTests(DatabaseFixture fixture)
{
    private static int _sequence = 40000;

    private sealed record Ctx(long Version, long Iteration, long Execution, int Rule, int DeletedRule, int OtherPhase, int OtherPhaseRule);

    private ExperimentDesignService Design() => new(new ExperimentDesignRepository(fixture.ConnectionFactory));

    private RecordingService Recording(ISqlConnectionFactory? factory = null)
    {
        var f = factory ?? fixture.ConnectionFactory;
        return new RecordingService(new RecordingRepository(f), new ExperimentDesignRepository(f));
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

    private Task<int> NewCategoryAsync() => ScalarAsync<int>(
        "INSERT dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES (@n, 1);",
        new { n = "ObsCat" + Guid.NewGuid().ToString("N") });

    private Task<int> NewPhaseAsync() => ScalarAsync<int>(
        "INSERT dbo.Phase (Name, Sequence) OUTPUT INSERTED.PhaseID VALUES (@n, @s);",
        new { n = "ObsPhase" + Guid.NewGuid().ToString("N"), s = Interlocked.Increment(ref _sequence) });

    private Task<int> NewRaterAsync() => ScalarAsync<int>(
        "INSERT dbo.Rater (Name, RaterType) OUTPUT INSERTED.RaterID VALUES (@n, 'Human');",
        new { n = "ObsRater" + Guid.NewGuid().ToString("N") });

    private async Task<long> NewVersionAsync() => (await Design().CreateExperimentVersionAsync(new CreateExperimentVersionRequest
    {
        Name = $"ObsVal-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
        EffectiveDateUTC = DateTime.UtcNow, Description = "observation validation test",
    })).ExperimentVersionID;

    private async Task<int> NewRuleAsync(long version, int phase, decimal weight, int min = 0, int max = 5, bool active = true) =>
        (await Design().CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = version, PhaseID = phase, ScoreCategoryID = await NewCategoryAsync(),
            WeightPercent = weight, MinScore = min, MaxScore = max, Active = active,
        })).ScoreRuleID;

    /// <summary>
    /// A version whose phase rules total 100 (so an Execution can be created), plus a deleted rule and a rule in
    /// another phase, all set up before the Execution exists: the design is frozen by the first Execution (Issue #4),
    /// so nothing about the rules can be arranged afterwards.
    /// </summary>
    private async Task<Ctx> SetupAsync(int min = 0, int max = 5, bool ruleActive = true)
    {
        var version = await NewVersionAsync();
        var rule = await NewRuleAsync(version, fixture.PhaseId, 100m, min, max, ruleActive);
        var deleted = await NewRuleAsync(version, fixture.PhaseId, 0m, min, max);
        await Design().DeleteScoreRuleAsync(deleted);
        var otherPhase = await NewPhaseAsync();
        var otherPhaseRule = await NewRuleAsync(version, otherPhase, 40m);

        var iteration = await Design().CreateIterationAsync(new CreateIterationRequest
        {
            ExperimentVersionID = version, Sequence = 1, IterationLabel = "i1", Prompt = "p",
        });
        var execution = await Recording().CreateExecutionAsync(new CreateExecutionRequest
        {
            IterationID = iteration.IterationID, AgentID = fixture.AgentId, PhaseID = fixture.PhaseId, StatusID = fixture.DraftStatusId,
            StartUTC = DateTime.UtcNow, AttemptNumber = 1,
        });
        return new Ctx(version, iteration.IterationID, execution.ExecutionID, rule, deleted, otherPhase, otherPhaseRule);
    }

    private CreateScoreObservationRequest Obs(long execution, int rule, decimal score, int? rater = null) => new()
    {
        ExecutionID = execution, ScoreRuleID = rule, RaterID = rater ?? fixture.RaterId, ObservedScore = score,
    };

    private Task<int> ObservationCountAsync(long execution) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreObservation WHERE ExecutionID = @execution;", new { execution });

    private Task LockAsync(long version) => LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, version);

    private static async Task AssertRejectedAsync(Func<Task> write, int sqlNumber, int httpStatus, string code)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(write);
        Assert.Equal(sqlNumber, ex.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(httpStatus, status);
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- creating observations

    [Fact]
    public async Task ValidObservation_ForAnUnlockedVersion_Succeeds()
    {
        var c = await SetupAsync();

        var created = await Recording().CreateScoreObservationAsync(new CreateScoreObservationRequest
        {
            ExecutionID = c.Execution, ScoreRuleID = c.Rule, RaterID = fixture.RaterId, ObservedScore = 3.5m,
            ObservedUTC = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc), ObservationNotes = "solid",
        });

        Assert.Equal(c.Execution, created.ExecutionID);
        Assert.Equal(c.Rule, created.ScoreRuleID);
        Assert.Equal(3.5m, created.ObservedScore);
        Assert.Equal(new DateTime(2026, 10, 1, 12, 30, 0), created.ObservedUTC);
        Assert.Equal("solid", created.ObservationNotes);
        Assert.Null(created.EvidenceURI);
    }

    [Theory]
    [InlineData(0, 5, "0", true)]
    [InlineData(0, 5, "5", true)]
    [InlineData(0, 5, "2.5", true)]
    [InlineData(0, 5, "-0.01", false)]
    [InlineData(0, 5, "5.01", false)]
    [InlineData(1, 10, "1", true)]
    [InlineData(1, 10, "10", true)]
    [InlineData(1, 10, "0.99", false)]
    [InlineData(1, 10, "10.01", false)]
    public async Task ScoreRange_IsInclusive_AtBothBoundaries(int min, int max, string score, bool allowed)
    {
        var c = await SetupAsync(min, max);

        if (allowed)
        {
            var created = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, D(score)));
            Assert.Equal(D(score), created.ObservedScore);
        }
        else
        {
            await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, D(score))),
                ErrorCatalog.OutOfRangeScore.SqlNumber!.Value, 400, "out-of-range-score");
            Assert.Equal(0, await ObservationCountAsync(c.Execution));
        }
    }

    [Fact]
    public async Task ARuleOfAnotherVersion_IsAVersionMismatch()
    {
        var c = await SetupAsync();
        var otherVersion = await NewVersionAsync();
        var foreignRule = await NewRuleAsync(otherVersion, fixture.PhaseId, 100m);

        await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, foreignRule, 3m)),
            ErrorCatalog.VersionPhaseMismatch.SqlNumber!.Value, 409, "version-phase-mismatch");
        Assert.Equal(0, await ObservationCountAsync(c.Execution));
    }

    [Fact]
    public async Task ARuleOfAnotherPhase_IsAPhaseMismatch()
    {
        var c = await SetupAsync();

        await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, c.OtherPhaseRule, 3m)),
            ErrorCatalog.VersionPhaseMismatch.SqlNumber!.Value, 409, "version-phase-mismatch");
        Assert.Equal(0, await ObservationCountAsync(c.Execution));
    }

    [Fact]
    public async Task ADeletedRule_CannotBeSelectedForANewObservation()
    {
        var c = await SetupAsync();

        // The rule no longer exists, so the foreign key refuses the observation, as for any unknown rule.
        await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, c.DeletedRule, 3m)),
            SqlErrorMapper.ForeignKeyViolation, 409, ErrorCatalog.ConstraintViolation.Code);
        Assert.Equal(0, await ObservationCountAsync(c.Execution));
    }

    [Fact]
    public async Task TheActiveFlag_IsNeverConsulted()
    {
        var c = await SetupAsync(ruleActive: false);

        var created = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));

        Assert.Equal(c.Rule, created.ScoreRuleID);
    }

    [Fact]
    public async Task ADuplicateExecutionRuleRaterTriple_IsRejected_ButAnotherRaterIsFine()
    {
        var c = await SetupAsync();
        await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));

        await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 4m)),
            SqlErrorMapper.UniqueConstraintViolation, 409, "duplicate-observation");

        var secondRater = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 4m, rater: await NewRaterAsync()));
        Assert.Equal(4m, secondRater.ObservedScore);
        Assert.Equal(2, await ObservationCountAsync(c.Execution));
    }

    [Fact]
    public async Task ALockedVersion_RejectsAValidObservation_AndTheLockIsReportedBeforeAnyContextError()
    {
        var c = await SetupAsync();
        var foreignRule = await NewRuleAsync(await NewVersionAsync(), fixture.PhaseId, 100m);
        await LockAsync(c.Version);

        await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m)),
            ErrorCatalog.VersionLocked.SqlNumber!.Value, 409, "version-locked");
        // Locked wins over a version mismatch and an out-of-range score alike.
        await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, foreignRule, 99m)),
            ErrorCatalog.VersionLocked.SqlNumber!.Value, 409, "version-locked");
        Assert.Equal(0, await ObservationCountAsync(c.Execution));
    }

    [Fact]
    public async Task AnUnknownExecution_IsNotFound()
    {
        var c = await SetupAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => Recording().CreateScoreObservationAsync(Obs(long.MaxValue, c.Rule, 3m)));

        var id = await new RecordingRepository(fixture.ConnectionFactory).CreateScoreObservationAsync(new Models.ScoreObservation
        {
            ExecutionID = long.MaxValue, ScoreRuleID = c.Rule, RaterID = fixture.RaterId, ObservedScore = 3m,
        });
        Assert.Null(id);
    }

    [Fact]
    public async Task AnUnknownRuleOrRater_StaysTheForeignKeyConflict()
    {
        var c = await SetupAsync();

        await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, int.MaxValue, 3m)),
            SqlErrorMapper.ForeignKeyViolation, 409, "constraint-violation");
        await AssertRejectedAsync(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m, rater: int.MaxValue)),
            SqlErrorMapper.ForeignKeyViolation, 409, "constraint-violation");
        Assert.Equal(0, await ObservationCountAsync(c.Execution));
    }

    [Fact]
    public async Task WorkInProgress_NeverBlocksAValidObservation()
    {
        // The Execution has no EndUTC, its phase is the only one scored, and no other observation exists anywhere.
        var c = await SetupAsync();
        Assert.Null((await Recording().GetExecutionAsync(c.Execution)).EndUTC);
        // (SetupAsync configured another phase's rule too, so the version has an unscored phase.)

        await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));

        // A finished Execution does not block scoring either.
        await Recording().UpdateExecutionRunFactsAsync(c.Execution, new UpdateExecutionRunFactsRequest { EndUTC = DateTime.UtcNow });
        await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 4m, rater: await NewRaterAsync()));
        Assert.Equal(2, await ObservationCountAsync(c.Execution));
    }

    // ---------------------------------------------------------------- content updates

    [Fact]
    public async Task ContentUpdate_WithinRange_ChangesOnlyTheContent()
    {
        var c = await SetupAsync();
        var original = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));

        var updated = await Recording().UpdateScoreObservationContentAsync(original.ScoreObservationID, new UpdateScoreObservationContentRequest
        {
            ObservedScore = 4.25m, ObservedUTC = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc), ObservationNotes = "revised",
        });

        Assert.Equal(4.25m, updated.ObservedScore);
        Assert.Equal(new DateTime(2026, 10, 2, 8, 0, 0), updated.ObservedUTC);
        Assert.Equal("revised", updated.ObservationNotes);
        Assert.Equal((original.ExecutionID, original.ScoreRuleID, original.RaterID), (updated.ExecutionID, updated.ScoreRuleID, updated.RaterID));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("5", true)]
    [InlineData("-0.01", false)]
    [InlineData("5.01", false)]
    public async Task ContentUpdate_ScoreRange_IsInclusive(string score, bool allowed)
    {
        var c = await SetupAsync();
        var original = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));
        var update = new UpdateScoreObservationContentRequest { ObservedScore = D(score), ObservationNotes = "attempt" };

        if (allowed)
        {
            Assert.Equal(D(score), (await Recording().UpdateScoreObservationContentAsync(original.ScoreObservationID, update)).ObservedScore);
        }
        else
        {
            await AssertRejectedAsync(() => Recording().UpdateScoreObservationContentAsync(original.ScoreObservationID, update),
                ErrorCatalog.OutOfRangeScore.SqlNumber!.Value, 400, "out-of-range-score");
            var unchanged = await Recording().GetScoreObservationAsync(original.ScoreObservationID);
            Assert.Equal(3m, unchanged.ObservedScore);
            Assert.Null(unchanged.ObservationNotes);
        }
    }

    [Fact]
    public async Task ContentUpdate_OnALockedVersion_IsRejected_AndNothingChanges()
    {
        var c = await SetupAsync();
        var original = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));
        await LockAsync(c.Version);

        await AssertRejectedAsync(
            () => Recording().UpdateScoreObservationContentAsync(original.ScoreObservationID, new UpdateScoreObservationContentRequest { ObservedScore = 4m }),
            ErrorCatalog.VersionLocked.SqlNumber!.Value, 409, "version-locked");

        Assert.Equal(3m, (await Recording().GetScoreObservationAsync(original.ScoreObservationID)).ObservedScore);
    }

    [Fact]
    public async Task ContentUpdate_OfAnUnknownObservation_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Recording().UpdateScoreObservationContentAsync(long.MaxValue, new UpdateScoreObservationContentRequest { ObservedScore = 3m }));
    }

    [Fact]
    public async Task TheObservationIdentity_StaysImmutable_ThroughTheDatabase()
    {
        var c = await SetupAsync();
        var original = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));
        var otherRater = await NewRaterAsync();

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            connection.ExecuteAsync($"UPDATE dbo.ScoreObservation SET RaterID = {otherRater} WHERE ScoreObservationID = {original.ScoreObservationID};"));

        Assert.Equal(ErrorCatalog.IdentityChangeRejected.SqlNumber, ex.Number);
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
    public async Task ConcurrentFinalization_CannotAdmitAnObservation_AfterTheVersionIsLocked()
    {
        var c = await SetupAsync();

        using var finalizer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var finalizerTransaction = finalizer.BeginTransaction();
        await AcquireAsync(finalizer, finalizerTransaction, c.Version, exclusive: true);

        var write = Task.Run(() => Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m)));
        await Task.Delay(500);
        Assert.False(write.IsCompleted, "the write must wait behind the finalization's exclusive lock");

        await LockedUtcTestHelper.LockAsync(finalizer, c.Version, finalizerTransaction);
        finalizerTransaction.Commit();

        var rejected = await Assert.ThrowsAsync<SqlException>(() => write.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, rejected.Number);
        Assert.Equal(0, await ObservationCountAsync(c.Execution));
    }

    [Fact]
    public async Task ConcurrentFinalization_CannotAdmitAContentUpdate_AfterTheVersionIsLocked()
    {
        var c = await SetupAsync();
        var original = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));

        using var finalizer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var finalizerTransaction = finalizer.BeginTransaction();
        await AcquireAsync(finalizer, finalizerTransaction, c.Version, exclusive: true);

        var write = Task.Run(() => Recording().UpdateScoreObservationContentAsync(
            original.ScoreObservationID, new UpdateScoreObservationContentRequest { ObservedScore = 4m }));
        await Task.Delay(500);
        Assert.False(write.IsCompleted);

        await LockedUtcTestHelper.LockAsync(finalizer, c.Version, finalizerTransaction);
        finalizerTransaction.Commit();

        var rejected = await Assert.ThrowsAsync<SqlException>(() => write.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, rejected.Number);
        Assert.Equal(3m, (await Recording().GetScoreObservationAsync(original.ScoreObservationID)).ObservedScore);
    }

    [Fact]
    public async Task WriteBlockedLongerThanTheLockTimeout_FailsRetryably_AndWritesNothing()
    {
        var c = await SetupAsync();
        var original = await Recording().CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 3m));

        using var holder = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var holderTransaction = holder.BeginTransaction();
        await AcquireAsync(holder, holderTransaction, c.Version, exclusive: true);

        var shortTimeout = Recording(fixture.CreateConnectionFactory(300));
        var createEx = await Assert.ThrowsAsync<SqlException>(() =>
            shortTimeout.CreateScoreObservationAsync(Obs(c.Execution, c.Rule, 4m, rater: fixture.RaterId + 1000)));
        var updateEx = await Assert.ThrowsAsync<SqlException>(() =>
            shortTimeout.UpdateScoreObservationContentAsync(original.ScoreObservationID, new UpdateScoreObservationContentRequest { ObservedScore = 4m }));
        holderTransaction.Rollback();

        foreach (var ex in new[] { createEx, updateEx })
        {
            Assert.Equal(ErrorCatalog.LockTimeout, ex.Number);
            var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
            Assert.Equal(409, status);
            Assert.Equal("retryable-conflict", body.GetProperty("code").GetString());
        }
        Assert.Equal(1, await ObservationCountAsync(c.Execution));
        Assert.Equal(3m, (await Recording().GetScoreObservationAsync(original.ScoreObservationID)).ObservedScore);
    }
}
