using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;
using ShepherdAPI.Api.Tests.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Issue #2: ExperimentVersion finalization (dbo.usp_FinalizeExperimentVersion, driven through the lock endpoint's
/// service) and the ScoreObservationSnapshot guard. Real SQL Server.
/// </summary>
[Collection("Database")]
public class FinalizationIntegrationTests(DatabaseFixture fixture)
{
    private const int VersionLocked = 50100;
    private const int SnapshotInsertNotPermitted = 50508;

    private static int _sequence = 80000;

    private ExperimentDesignService Design() => new(new ExperimentDesignRepository(fixture.ConnectionFactory));

    private RecordingService Recording() =>
        new(new RecordingRepository(fixture.ConnectionFactory), new ExperimentDesignRepository(fixture.ConnectionFactory), new LockGuardQueries(fixture.ConnectionFactory));

    // ---------------------------------------------------------------- helpers

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private async Task ExecAsync(string sql, object? parameters = null)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private Task<int> NewPhaseAsync() => ScalarAsync<int>(
        "INSERT dbo.Phase (Name, Sequence) OUTPUT INSERTED.PhaseID VALUES (@n, @s);",
        new { n = "FinPhase" + Guid.NewGuid().ToString("N"), s = Interlocked.Increment(ref _sequence) });

    private Task<int> NewCategoryAsync(bool higherIsBetter = true) => ScalarAsync<int>(
        "INSERT dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES (@n, @h);",
        new { n = "FinCat" + Guid.NewGuid().ToString("N"), h = higherIsBetter });

    private Task<int> NewRaterAsync() => ScalarAsync<int>(
        "INSERT dbo.Rater (Name, RaterType) OUTPUT INSERTED.RaterID VALUES (@n, 'Human');",
        new { n = "FinRater" + Guid.NewGuid().ToString("N") });

    private async Task<ExperimentVersion> NewVersionAsync() => await Design().CreateExperimentVersionAsync(new CreateExperimentVersionRequest
    {
        Name = $"Fin-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
        EffectiveDateUTC = DateTime.UtcNow, Description = "finalization test",
    });

    private async Task<int> NewRuleAsync(long version, int phase, decimal weight, int category, bool active = true, int min = 0, int max = 5) =>
        (await Design().CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = version, PhaseID = phase, ScoreCategoryID = category,
            WeightPercent = weight, MinScore = min, MaxScore = max, Active = active, RuleNotes = "fin",
        })).ScoreRuleID;

    private Task<Execution> NewExecutionAsync(long iteration, int phase, int attempt) => Recording().CreateExecutionAsync(new CreateExecutionRequest
    {
        IterationID = iteration, AgentID = fixture.AgentId, PhaseID = phase, StatusID = fixture.DraftStatusId,
        StartUTC = DateTime.UtcNow, AttemptNumber = attempt,
    });

    private Task<ScoreObservation> ObserveAsync(long execution, int rule, int rater, decimal score) =>
        Recording().CreateScoreObservationAsync(new CreateScoreObservationRequest
        {
            ExecutionID = execution, ScoreRuleID = rule, RaterID = rater, ObservedScore = score,
        });

    private Task EndAsync(long execution) =>
        Recording().UpdateExecutionRunFactsAsync(execution, new UpdateExecutionRunFactsRequest { EndUTC = DateTime.UtcNow });

    /// <summary>
    /// A finalizable version: phase A (rules 60 [Active = false] + 40) and phase B (one rule at 100), three ended
    /// Executions (two in A, one in B), two raters, six observations. Optionally a third configured phase C with a
    /// rule but no Execution (so finalization must refuse it).
    /// </summary>
    private sealed record Fin(
        long Version, int PhaseA, int PhaseB, int PhaseC, int RuleA1, int RuleA2, int RuleB, int CategoryA1, int CategoryA2, int CategoryB,
        long Iteration, long E1, long E2, long E3, int Rater1, int Rater2, IReadOnlyList<ScoreObservation> Observations);

    private async Task<Fin> BuildAsync(bool thirdConfiguredPhase = false, bool endExecutions = true, bool observe = true)
    {
        var version = (await NewVersionAsync()).ExperimentVersionID;
        var phaseA = fixture.PhaseId;
        var phaseB = await NewPhaseAsync();
        var phaseC = 0;
        var catA1 = await NewCategoryAsync(higherIsBetter: true);
        var catA2 = await NewCategoryAsync(higherIsBetter: false);
        var catB = await NewCategoryAsync(higherIsBetter: true);

        var ruleA1 = await NewRuleAsync(version, phaseA, 60m, catA1, active: false); // Active must not affect eligibility
        var ruleA2 = await NewRuleAsync(version, phaseA, 40m, catA2);
        var ruleB = await NewRuleAsync(version, phaseB, 100m, catB, min: 1, max: 10);
        if (thirdConfiguredPhase)
        {
            phaseC = await NewPhaseAsync();
            await NewRuleAsync(version, phaseC, 100m, await NewCategoryAsync());
        }

        var iteration = (await Design().CreateIterationAsync(new CreateIterationRequest
            { ExperimentVersionID = version, Sequence = 1, IterationLabel = "i1", Prompt = "p" })).IterationID;
        var e1 = (await NewExecutionAsync(iteration, phaseA, 1)).ExecutionID;
        var e2 = (await NewExecutionAsync(iteration, phaseA, 2)).ExecutionID;
        var e3 = (await NewExecutionAsync(iteration, phaseB, 1)).ExecutionID;
        var rater1 = fixture.RaterId;
        var rater2 = await NewRaterAsync();

        var observations = new List<ScoreObservation>();
        if (observe)
        {
            observations.Add(await ObserveAsync(e1, ruleA1, rater1, 3.25m));
            observations.Add(await ObserveAsync(e1, ruleA2, rater1, 4m));
            observations.Add(await ObserveAsync(e1, ruleA1, rater2, 5m));
            observations.Add(await ObserveAsync(e2, ruleA1, rater1, 0m));
            observations.Add(await ObserveAsync(e2, ruleA2, rater1, 2.5m));
            observations.Add(await ObserveAsync(e3, ruleB, rater1, 10m));
        }

        if (endExecutions)
        {
            await EndAsync(e1);
            await EndAsync(e2);
            await EndAsync(e3);
        }

        return new Fin(version, phaseA, phaseB, phaseC, ruleA1, ruleA2, ruleB, catA1, catA2, catB, iteration, e1, e2, e3, rater1, rater2, observations);
    }

    private static readonly LockExperimentVersionRequest NoStatus = new();

    private Task<ExperimentVersion> FinalizeAsync(long version, LockExperimentVersionRequest? request = null) =>
        Design().LockExperimentVersionAsync(version, request ?? NoStatus);

    private Task<int> SnapshotCountAsync(long version) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = @version;", new { version });

    private async Task<DateTime?> LockedUtcAsync(long version) =>
        await ScalarAsync<DateTime?>("SELECT LockedUTC FROM dbo.ExperimentVersion WHERE ExperimentVersionID = @version;", new { version });

    /// <summary>Runs the action with one trigger disabled in the disposable test database, always re-enabling it.</summary>
    private async Task WithTriggerDisabledAsync(string table, string trigger, Func<Task> action)
    {
        await ExecAsync($"DISABLE TRIGGER dbo.{trigger} ON dbo.{table};");
        try
        {
            await action();
        }
        finally
        {
            await ExecAsync($"ENABLE TRIGGER dbo.{trigger} ON dbo.{table};");
        }
    }

    /// <summary>Finalizes expecting the given catalog failure; returns the mapped HTTP status and ProblemDetails body.</summary>
    private async Task<System.Text.Json.JsonElement> FailsAsync(long version, string code, int expectedLocks = 0)
    {
        Exception thrown;
        try
        {
            await FinalizeAsync(version);
            throw new Xunit.Sdk.XunitException($"finalization was expected to fail with {code}, but it succeeded");
        }
        catch (Exception ex) when (ex is CatalogException or SqlException)
        {
            thrown = ex;
        }

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        Assert.Equal(409, status);
        Assert.Equal(code, body.GetProperty("code").GetString());

        // A failed attempt leaves nothing behind.
        Assert.Null(await LockedUtcAsync(version));
        Assert.Equal(expectedLocks, await SnapshotCountAsync(version));
        return body;
    }

    private static long[] Ids(System.Text.Json.JsonElement body, string member) =>
        body.GetProperty(member).EnumerateArray().Select(e => e.GetInt64()).ToArray();

    private static async Task AcquireAsync(IDbConnection connection, IDbTransaction transaction, long versionId, bool exclusive = false)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", versionId);
        parameters.Add("@Exclusive", exclusive);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_AcquireVersionLock", parameters, transaction, commandType: CommandType.StoredProcedure);
    }

    // ---------------------------------------------------------------- deployment

    [Fact]
    public async Task FinalizationObjects_AreDeployed_AndTheUpgradeIsRerunnable()
    {
        Assert.NotNull(await ScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.usp_FinalizeExperimentVersion');"));
        Assert.NotNull(await ScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.trg_ScoreObservationSnapshot_Guard');"));
        Assert.Equal(3, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ScoreObservationSnapshot') AND name LIKE N'CK[_]ScoreObservationSnapshot[_]%';"));

        await fixture.ApplyDeploymentScriptsAsync();
        await fixture.ApplyDeploymentScriptsAsync();
        Assert.Equal(3, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ScoreObservationSnapshot') AND name LIKE N'CK[_]ScoreObservationSnapshot[_]%';"));

        Assert.Equal(6, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.key_constraints kc JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id WHERE kc.name = N'PK_ScoreObservationSnapshot';"));
    }

    // ---------------------------------------------------------------- success

    [Fact]
    public async Task Finalization_WritesOneSnapshotRowPerObservation_WithTheCopiedValues_AndOneTimestamp()
    {
        var f = await BuildAsync();
        var statusBefore = (await Design().GetExperimentVersionAsync(f.Version)).StatusID;

        // The optional StatusID in the request is ignored.
        var finalized = await FinalizeAsync(f.Version, new LockExperimentVersionRequest { StatusID = fixture.RetiredStatusId });

        Assert.NotNull(finalized.LockedUTC);
        Assert.Equal(statusBefore, finalized.StatusID);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var rows = (await connection.QueryAsync<SnapshotRow>(
            "SELECT * FROM dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = @v ORDER BY ScoreObservationID;", new { v = f.Version })).ToList();

        Assert.Equal(6, f.Observations.Count);
        Assert.Equal(f.Observations.Count, rows.Count); // all raters, one row each
        Assert.Equal(f.Observations.Select(o => o.ScoreObservationID), rows.Select(r => r.ScoreObservationID));

        var rules = new Dictionary<int, (int Category, decimal Weight, int Min, int Max, bool Higher)>
        {
            [f.RuleA1] = (f.CategoryA1, 60m, 0, 5, true),
            [f.RuleA2] = (f.CategoryA2, 40m, 0, 5, false),
            [f.RuleB] = (f.CategoryB, 100m, 1, 10, true),
        };
        var executionPhase = new Dictionary<long, int> { [f.E1] = f.PhaseA, [f.E2] = f.PhaseA, [f.E3] = f.PhaseB };

        foreach (var (observation, row) in f.Observations.Zip(rows))
        {
            var rule = rules[observation.ScoreRuleID];
            Assert.Equal(f.Version, row.ExperimentVersionID);
            Assert.Equal(observation.ExecutionID, row.ExecutionID);
            Assert.Equal(executionPhase[observation.ExecutionID], row.PhaseID);
            Assert.Equal(rule.Category, row.ScoreCategoryID);
            Assert.Equal(observation.ScoreRuleID, row.ScoreRuleID);
            Assert.Equal(observation.ObservedScore, row.ObservedScore); // copied, never calculated
            Assert.Equal(rule.Min, row.MinScore);
            Assert.Equal(rule.Max, row.MaxScore);
            Assert.Equal(rule.Weight, row.WeightPercent);
            Assert.Equal(rule.Higher, row.HigherIsBetter);
        }

        // One database-generated timestamp for the lock and every row.
        Assert.All(rows, r => Assert.Equal(finalized.LockedUTC, r.SnapshotUTC));
        Assert.Equal(finalized.LockedUTC, await LockedUtcAsync(f.Version));
    }

    private sealed class SnapshotRow
    {
        public long ExperimentVersionID { get; set; }
        public long ExecutionID { get; set; }
        public int PhaseID { get; set; }
        public int ScoreCategoryID { get; set; }
        public int ScoreRuleID { get; set; }
        public long ScoreObservationID { get; set; }
        public decimal ObservedScore { get; set; }
        public int MinScore { get; set; }
        public int MaxScore { get; set; }
        public decimal WeightPercent { get; set; }
        public bool HigherIsBetter { get; set; }
        public DateTime SnapshotUTC { get; set; }
    }

    [Fact]
    public async Task TheSnapshot_IsAuthoritative_LaterChangesToRulesAndCategoriesDoNotTouchIt()
    {
        var f = await BuildAsync();
        await FinalizeAsync(f.Version);

        async Task<List<SnapshotRow>> Rows()
        {
            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            return (await connection.QueryAsync<SnapshotRow>(
                "SELECT * FROM dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = @v ORDER BY ScoreObservationID;", new { v = f.Version })).ToList();
        }

        var before = await Rows();

        // A later category edit does not change the HigherIsBetter copied into the snapshot...
        await ExecAsync("UPDATE dbo.ScoreCategory SET HigherIsBetter = 1 - HigherIsBetter WHERE ScoreCategoryID IN (@a, @b);", new { a = f.CategoryA1, b = f.CategoryA2 });
        // ...and the originating rule cannot change after finalization (#7: no post-lock exception)...
        var update = await Assert.ThrowsAsync<SqlException>(() => Design().UpdateScoreRuleAsync(f.RuleA2, new UpdateScoreRuleRequest { WeightPercent = 1m, MinScore = 0, MaxScore = 5 }));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, update.Number);
        var delete = await Assert.ThrowsAsync<SqlException>(() => Design().DeleteScoreRuleAsync(f.RuleA2));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, delete.Number);

        // ...nor be deleted even with the guard trigger bypassed: the observation and snapshot foreign keys keep it.
        await WithTriggerDisabledAsync("ScoreRule", "trg_ScoreRule_Guard", async () =>
        {
            var raw = await Assert.ThrowsAsync<SqlException>(() => ExecAsync("DELETE dbo.ScoreRule WHERE ScoreRuleID = @r;", new { r = f.RuleA2 }));
            Assert.Equal(547, raw.Number);
        });
        Assert.Equal(40m, (await Design().GetScoreRuleAsync(f.RuleA2)).WeightPercent);

        var after = await Rows();
        Assert.Equal(before.Count, after.Count);
        foreach (var (b, a) in before.Zip(after))
        {
            Assert.Equal((b.ScoreRuleID, b.ScoreCategoryID, b.ObservedScore, b.MinScore, b.MaxScore, b.WeightPercent, b.HigherIsBetter, b.SnapshotUTC),
                         (a.ScoreRuleID, a.ScoreCategoryID, a.ObservedScore, a.MinScore, a.MaxScore, a.WeightPercent, a.HigherIsBetter, a.SnapshotUTC));
        }

        Assert.Contains(after, r => r.ScoreRuleID == f.RuleA2);
    }

    [Fact]
    public async Task ScoreRuleActive_DoesNotAffectEligibility()
    {
        // BuildAsync's 60% rule is Active = false and the version still finalizes with that rule's observations included.
        var f = await BuildAsync();
        await FinalizeAsync(f.Version);

        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = @v AND ScoreRuleID = @r;", new { v = f.Version, r = f.RuleA1 }));
    }

    [Fact]
    public async Task FinalizingOneVersion_DoesNotTouchAnother()
    {
        var f = await BuildAsync();
        var other = await BuildAsync();
        await FinalizeAsync(f.Version);

        Assert.Equal(0, await SnapshotCountAsync(other.Version));
        Assert.Null(await LockedUtcAsync(other.Version));
        await FinalizeAsync(other.Version);
        Assert.Equal(6, await SnapshotCountAsync(other.Version));
    }

    [Fact]
    public async Task ThePostFinalizationVersion_StillAcceptsStatusChanges()
    {
        var f = await BuildAsync();
        await FinalizeAsync(f.Version);

        var retired = await Design().SetExperimentVersionStatusAsync(f.Version, new SetExperimentVersionStatusRequest { StatusID = fixture.RetiredStatusId });
        Assert.Equal(fixture.RetiredStatusId, retired.StatusID);
        Assert.NotNull(retired.LockedUTC);
    }

    [Fact]
    public async Task FinalizingAMissingVersion_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => FinalizeAsync(long.MaxValue));
    }

    // ---------------------------------------------------------------- failed checks

    [Fact]
    public async Task AlreadyLocked_IsTheVersionLockedError_NotASecondFinalization()
    {
        var f = await BuildAsync();
        var first = await FinalizeAsync(f.Version);

        var body = await FailsAsyncAfterFinalization(f.Version, "version-locked");

        Assert.Equal(first.LockedUTC, await LockedUtcAsync(f.Version)); // unchanged
        Assert.Equal(6, await SnapshotCountAsync(f.Version));
        Assert.NotNull(body);
    }

    private async Task<System.Text.Json.JsonElement> FailsAsyncAfterFinalization(long version, string code)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => FinalizeAsync(version));
        Assert.Equal(VersionLocked, ex.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }

    [Fact]
    public async Task NoExecutions_IsFinalizeNoExecutions()
    {
        var version = (await NewVersionAsync()).ExperimentVersionID;
        await NewRuleAsync(version, fixture.PhaseId, 100m, await NewCategoryAsync());

        await FailsAsync(version, "finalize-no-executions");
    }

    [Fact]
    public async Task AnExecutionWithoutEndUtc_IsFinalizeExecutionNotEnded_NamingIt()
    {
        var f = await BuildAsync(endExecutions: false);
        await EndAsync(f.E1);
        await EndAsync(f.E3);

        var body = await FailsAsync(f.Version, "finalize-execution-not-ended");

        Assert.Equal([f.E2], Ids(body, "executionIDs"));
        Assert.Equal(1, body.GetProperty("total").GetInt64());
        Assert.DoesNotContain("[ExecutionIDs", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AnExecutionWithoutObservation_IsFinalizeExecutionWithoutObservation_NamingIt()
    {
        var f = await BuildAsync();
        var bare = (await NewExecutionAsyncAfterDesign(f)).ExecutionID; // see helper: a fourth Execution with no observation
        await EndAsync(bare);

        var body = await FailsAsync(f.Version, "finalize-execution-without-observation");

        Assert.Equal([bare], Ids(body, "executionIDs"));
    }

    private Task<Execution> NewExecutionAsyncAfterDesign(Fin f) => NewExecutionAsync(f.Iteration, f.PhaseA, 9);

    [Fact]
    public async Task AnExecutionInAnUnconfiguredPhase_IsFinalizeExecutionPhaseNotConfigured_NamingExecutionAndPhase()
    {
        var f = await BuildAsync();

        // Normal flow cannot reach this state (a live version's rules cannot change), and phase B's only rule is
        // observed, so it cannot be deleted either: move it to an unused phase behind the guard trigger's back.
        var unusedPhase = await NewPhaseAsync();
        await WithTriggerDisabledAsync("ScoreRule", "trg_ScoreRule_Guard",
            () => ExecAsync("UPDATE dbo.ScoreRule SET PhaseID = @p WHERE ScoreRuleID = @r;", new { p = unusedPhase, r = f.RuleB }));

        var body = await FailsAsync(f.Version, "finalize-execution-phase-not-configured");

        Assert.Equal([f.E3], Ids(body, "executionIDs"));
        Assert.Equal([(long)f.PhaseB], Ids(body, "phaseIDs"));
    }

    [Fact]
    public async Task AConfiguredPhaseWithoutObservation_IsFinalizePhaseWithoutObservation_NamingThePhase()
    {
        // The version has a third configured phase (a rule at 100) in which no Execution was ever run.
        var f = await BuildAsync(thirdConfiguredPhase: true);

        var body = await FailsAsync(f.Version, "finalize-phase-without-observation");

        Assert.Equal([(long)f.PhaseC], Ids(body, "phaseIDs"));
    }

    [Fact]
    public async Task AConfiguredPhaseWhoseRulesDoNotTotal100_IsFinalizePhaseWeightInvalid_NamingThePhase()
    {
        var f = await BuildAsync();

        // Execution creation requires 100.00, and a live version's rules cannot change, so add a stray rule behind the guard.
        var stray = await NewCategoryAsync();
        await WithTriggerDisabledAsync("ScoreRule", "trg_ScoreRule_Guard", () => ExecAsync(
            "INSERT dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active) VALUES (@v, @p, @c, 5, 0, 5, 1);",
            new { v = f.Version, p = f.PhaseB, c = stray }));

        var body = await FailsAsync(f.Version, "finalize-phase-weight-invalid");

        Assert.Equal([(long)f.PhaseB], Ids(body, "phaseIDs"));
        Assert.Contains("105.00", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AnOutOfRangeObservation_IsFinalizeObservationIntegrity_NamingIt()
    {
        var f = await BuildAsync();
        var victim = f.Observations[1].ScoreObservationID; // rule A2, range 0..5
        await ExecAsync("UPDATE dbo.ScoreObservation SET ObservedScore = 5.01 WHERE ScoreObservationID = @id;", new { id = victim });

        var body = await FailsAsync(f.Version, "finalize-observation-integrity");

        Assert.Equal([victim], Ids(body, "scoreObservationIDs"));
    }

    [Fact]
    public async Task TheRangeBoundaries_AreInclusive()
    {
        var f = await BuildAsync();
        // Rule A1 is 0..5 and B is 1..10; BuildAsync already scored 0 and 5 and 10 (the inclusive bounds).
        await FinalizeAsync(f.Version);
        Assert.Equal(6, await SnapshotCountAsync(f.Version));
    }

    [Fact]
    public async Task AnObservationWhoseRuleBelongsToAnotherPhaseOrVersion_IsFinalizeObservationIntegrity()
    {
        var f = await BuildAsync();
        var other = await BuildAsync();

        // Raw inserts bypass the controlled-write procedure's validation (Issue #3): a rule of another phase, then of another version.
        var phaseMismatch = await ScalarAsync<long>(
            "INSERT dbo.ScoreObservation (ExecutionID, ScoreRuleID, RaterID, ObservedScore) VALUES (@e, @r, @rater, 3); SELECT CAST(SCOPE_IDENTITY() AS bigint);",
            new { e = f.E1, r = f.RuleB, rater = f.Rater2 });
        var versionMismatch = await ScalarAsync<long>(
            "INSERT dbo.ScoreObservation (ExecutionID, ScoreRuleID, RaterID, ObservedScore) VALUES (@e, @r, @rater, 3); SELECT CAST(SCOPE_IDENTITY() AS bigint);",
            new { e = f.E2, r = other.RuleA1, rater = f.Rater2 });

        var body = await FailsAsync(f.Version, "finalize-observation-integrity");

        Assert.Equal(new[] { phaseMismatch, versionMismatch }.OrderBy(i => i), Ids(body, "scoreObservationIDs"));
    }

    [Fact]
    public async Task PreExistingSnapshotRows_RefuseFinalization()
    {
        var f = await BuildAsync();
        var observation = f.Observations[0];

        // A stray snapshot row for an unlocked version, written the only way the guard allows: with the token.
        await ExecAsync($"""
            EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = {f.Version};
            INSERT dbo.ScoreObservationSnapshot (ExperimentVersionID, ExecutionID, PhaseID, ScoreCategoryID, ScoreRuleID, ScoreObservationID,
                                                 ObservedScore, MinScore, MaxScore, WeightPercent, HigherIsBetter, SnapshotUTC)
            VALUES ({f.Version}, {observation.ExecutionID}, {f.PhaseA}, {f.CategoryA1}, {f.RuleA1}, {observation.ScoreObservationID}, 3.25, 0, 5, 60, 1, SYSUTCDATETIME());
            EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = NULL;
            """);

        var body = await FailsAsync(f.Version, "finalize-snapshot-rows-exist", expectedLocks: 1);

        Assert.Equal(1, body.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task EachFailedCheck_HasItsOwnCode()
    {
        var codes = new[]
        {
            ErrorCatalog.FinalizeNoExecutions, ErrorCatalog.FinalizeExecutionNotEnded, ErrorCatalog.FinalizeExecutionWithoutObservation,
            ErrorCatalog.FinalizeExecutionPhaseNotConfigured, ErrorCatalog.FinalizePhaseWithoutObservation, ErrorCatalog.FinalizePhaseWeightInvalid,
            ErrorCatalog.FinalizeObservationIntegrity, ErrorCatalog.FinalizeSnapshotRowsExist, ErrorCatalog.VersionLocked,
        }.Select(e => e.Code);
        Assert.Equal(9, codes.Distinct().Count());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChecksRunInTheDocumentedOrder_WhenSeveralFail()
    {
        // Not ended AND without an observation: the not-ended check comes first.
        var f = await BuildAsync(endExecutions: false, observe: false);
        await FailsAsync(f.Version, "finalize-execution-not-ended");

        // Ended but no observations: now the observation check fires.
        await EndAsync(f.E1);
        await EndAsync(f.E2);
        await EndAsync(f.E3);
        await FailsAsync(f.Version, "finalize-execution-without-observation");
    }

    // ---------------------------------------------------------------- rollback

    [Fact]
    public async Task AFailureAfterTheInserts_RollsBackEverything_AndClearsTheToken()
    {
        var f = await BuildAsync();

        // A test-only trigger makes the very last step (setting LockedUTC) fail, after the snapshot rows were inserted.
        await ExecAsync("""
            CREATE OR ALTER TRIGGER dbo.trg_TestFailLock ON dbo.ExperimentVersion AFTER UPDATE AS
            BEGIN
                IF UPDATE(LockedUTC) THROW 59999, N'forced failure after the snapshot inserts', 1;
            END
            """);
        try
        {
            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            var parameters = new DynamicParameters();
            parameters.Add("@ExperimentVersionID", f.Version);
            parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("dbo.usp_FinalizeExperimentVersion", parameters, commandType: CommandType.StoredProcedure));

            Assert.Equal(59999, ex.Number); // the original error is rethrown, not wrapped
            Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
            Assert.Null(await connection.QuerySingleAsync<long?>("SELECT TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));"));
        }
        finally
        {
            await ExecAsync("DROP TRIGGER IF EXISTS dbo.trg_TestFailLock;");
        }

        Assert.Equal(0, await SnapshotCountAsync(f.Version));
        Assert.Null(await LockedUtcAsync(f.Version));

        // The version is still fully finalizable afterwards.
        await FinalizeAsync(f.Version);
        Assert.Equal(6, await SnapshotCountAsync(f.Version));
    }

    [Fact]
    public async Task AFailureDuringTheSnapshotInsert_RollsBackEverything_AndClearsTheToken()
    {
        var f = await BuildAsync();

        // A test-only trigger makes the snapshot insert itself fail, while the finalization token is set.
        await ExecAsync("""
            CREATE OR ALTER TRIGGER dbo.trg_TestFailSnapshot ON dbo.ScoreObservationSnapshot AFTER INSERT AS
            BEGIN
                THROW 59998, N'forced failure during the snapshot insert', 1;
            END
            """);
        try
        {
            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            var parameters = new DynamicParameters();
            parameters.Add("@ExperimentVersionID", f.Version);
            parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("dbo.usp_FinalizeExperimentVersion", parameters, commandType: CommandType.StoredProcedure));

            Assert.Equal(59998, ex.Number);
            Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
            Assert.Null(await connection.QuerySingleAsync<long?>("SELECT TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));"));
        }
        finally
        {
            await ExecAsync("DROP TRIGGER IF EXISTS dbo.trg_TestFailSnapshot;");
        }

        Assert.Equal(0, await SnapshotCountAsync(f.Version));
        Assert.Null(await LockedUtcAsync(f.Version));
        await FinalizeAsync(f.Version);
        Assert.Equal(6, await SnapshotCountAsync(f.Version));
    }

    [Fact]
    public async Task TheLockedUtcUpdate_IsAuthorizedByTheTokenInsideFinalization_AndByNothingElse()
    {
        var f = await BuildAsync();

        // Nothing but finalization can lock a finalizable version: a raw assignment without the key is refused...
        using (var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync())
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                "UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = @v;", new { v = f.Version }));
            Assert.Equal(50509, ex.Number);
        }

        Assert.Null(await LockedUtcAsync(f.Version));

        // ...while finalization itself locks it, with the snapshot, and leaves the key NULL on its connection.
        var finalized = await FinalizeAsync(f.Version);
        Assert.Equal(finalized.LockedUTC, await LockedUtcAsync(f.Version));
        Assert.Equal(6, await SnapshotCountAsync(f.Version));
    }

    [Fact]
    public async Task TheToken_IsClearedAfterSuccessAndAfterAFailedCheck()
    {
        var failing = await BuildAsync(endExecutions: false);
        var good = await BuildAsync();

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        async Task Run(long version)
        {
            var parameters = new DynamicParameters();
            parameters.Add("@ExperimentVersionID", version);
            parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
            await connection.ExecuteAsync("dbo.usp_FinalizeExperimentVersion", parameters, commandType: CommandType.StoredProcedure);
        }

        await Assert.ThrowsAsync<SqlException>(() => Run(failing.Version));
        Assert.Null(await connection.QuerySingleAsync<long?>("SELECT TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));"));
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;")); // no procedure-owned transaction left open

        await Run(good.Version);
        Assert.Null(await connection.QuerySingleAsync<long?>("SELECT TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));"));
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
    }

    // ---------------------------------------------------------------- snapshot immutability and insert control

    [Fact]
    public async Task SnapshotRows_CannotBeUpdatedOrDeleted()
    {
        var f = await BuildAsync();
        await FinalizeAsync(f.Version);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        foreach (var sql in new[]
        {
            $"UPDATE dbo.ScoreObservationSnapshot SET ObservedScore = 1 WHERE ExperimentVersionID = {f.Version};",
            $"UPDATE dbo.ScoreObservationSnapshot SET HigherIsBetter = 1 - HigherIsBetter WHERE ExperimentVersionID = {f.Version};",
            $"UPDATE dbo.ScoreObservationSnapshot SET SnapshotUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = {f.Version};",
            $"UPDATE dbo.ScoreObservationSnapshot SET ScoreRuleID = ScoreRuleID WHERE ExperimentVersionID = {f.Version};",
            $"DELETE dbo.ScoreObservationSnapshot WHERE ScoreObservationID = {f.Observations[0].ScoreObservationID};",
            $"DELETE dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = {f.Version};",
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
            Assert.True(ex.Number == VersionLocked, $"expected {VersionLocked} but got {ex.Number} ({ex.Message}) for: {sql}");
        }

        Assert.Equal(6, await SnapshotCountAsync(f.Version));
    }

    [Fact]
    public async Task SnapshotRows_CannotBeUpdatedOrDeleted_EvenWhenTheTokenIsSet()
    {
        var f = await BuildAsync();
        await FinalizeAsync(f.Version);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync($"EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = {f.Version};");
        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync($"UPDATE dbo.ScoreObservationSnapshot SET ObservedScore = 1 WHERE ExperimentVersionID = {f.Version};"));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync($"DELETE dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = {f.Version};"));
        Assert.Equal(VersionLocked, update.Number);
        Assert.Equal(VersionLocked, delete.Number);
        Assert.Equal(6, await SnapshotCountAsync(f.Version));
    }

    private static string InsertSql(long version, long execution, int phase, int category, int rule, long observation, string values = "3, 0, 5, 50, 1") =>
        $"""
        INSERT dbo.ScoreObservationSnapshot (ExperimentVersionID, ExecutionID, PhaseID, ScoreCategoryID, ScoreRuleID, ScoreObservationID,
                                             ObservedScore, MinScore, MaxScore, WeightPercent, HigherIsBetter, SnapshotUTC)
        VALUES ({version}, {execution}, {phase}, {category}, {rule}, {observation}, {values}, SYSUTCDATETIME());
        """;

    [Fact]
    public async Task SnapshotInserts_AreRejectedOutsideTheProcedure()
    {
        var f = await BuildAsync();
        var o = f.Observations[0];
        var sql = InsertSql(f.Version, o.ExecutionID, f.PhaseA, f.CategoryA1, f.RuleA1, o.ScoreObservationID);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();

        // No token at all.
        var noToken = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
        Assert.Equal(SnapshotInsertNotPermitted, noToken.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(noToken);
        Assert.Equal(409, status);
        Assert.Equal("snapshot-insert-not-permitted", body.GetProperty("code").GetString());

        // A token for a different version.
        await connection.ExecuteAsync($"EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = {f.Version + 1000};");
        var wrongVersion = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
        Assert.Equal(SnapshotInsertNotPermitted, wrongVersion.Number);

        // A garbage token.
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = N'not a number';");
        var garbage = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
        Assert.Equal(SnapshotInsertNotPermitted, garbage.Number);

        Assert.Equal(0, await SnapshotCountAsync(f.Version));
    }

    [Fact]
    public async Task SnapshotInserts_AreRejectedForALockedVersion_EvenWithTheToken()
    {
        var f = await BuildAsync();
        var o = f.Observations[0];
        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, f.Version);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync($"EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = {f.Version};");
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            InsertSql(f.Version, o.ExecutionID, f.PhaseA, f.CategoryA1, f.RuleA1, o.ScoreObservationID)));
        Assert.Equal(VersionLocked, ex.Number);
        Assert.Equal(0, await SnapshotCountAsync(f.Version));
    }

    [Fact]
    public async Task SnapshotInserts_MixingVersions_AreRejectedWhole()
    {
        var f = await BuildAsync();
        var other = await BuildAsync();
        var a = f.Observations[0];
        var b = other.Observations[0];

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync($"EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = {f.Version};");
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync($"""
            INSERT dbo.ScoreObservationSnapshot (ExperimentVersionID, ExecutionID, PhaseID, ScoreCategoryID, ScoreRuleID, ScoreObservationID,
                                                 ObservedScore, MinScore, MaxScore, WeightPercent, HigherIsBetter, SnapshotUTC)
            VALUES ({f.Version}, {a.ExecutionID}, {f.PhaseA}, {f.CategoryA1}, {f.RuleA1}, {a.ScoreObservationID}, 3, 0, 5, 60, 1, SYSUTCDATETIME()),
                   ({other.Version}, {b.ExecutionID}, {other.PhaseA}, {other.CategoryA1}, {other.RuleA1}, {b.ScoreObservationID}, 3, 0, 5, 60, 1, SYSUTCDATETIME());
            """));
        Assert.Equal(SnapshotInsertNotPermitted, ex.Number);
        Assert.Equal(0, await SnapshotCountAsync(f.Version));
        Assert.Equal(0, await SnapshotCountAsync(other.Version));
    }

    [Fact]
    public async Task TheCheckConstraints_RejectInvalidSnapshotValues_EvenWithTheToken()
    {
        var f = await BuildAsync();
        var o = f.Observations[0];

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync($"EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = {f.Version};");

        async Task Rejects(string values, string constraint)
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                InsertSql(f.Version, o.ExecutionID, f.PhaseA, f.CategoryA1, f.RuleA1, o.ScoreObservationID, values)));
            Assert.Equal(547, ex.Number);
            Assert.Contains(constraint, ex.Message);
        }

        await Rejects("3, 5, 5, 50, 1", "CK_ScoreObservationSnapshot_MinScore_LT_MaxScore");
        await Rejects("3, 6, 5, 50, 1", "CK_ScoreObservationSnapshot_MinScore_LT_MaxScore");
        await Rejects("3, 0, 5, 100.01, 1", "CK_ScoreObservationSnapshot_WeightPercent");
        await Rejects("3, 0, 5, -0.01, 1", "CK_ScoreObservationSnapshot_WeightPercent");
        await Rejects("5.01, 0, 5, 50, 1", "CK_ScoreObservationSnapshot_ObservedScore_InRange");
        await Rejects("-0.01, 0, 5, 50, 1", "CK_ScoreObservationSnapshot_ObservedScore_InRange");

        // The boundaries themselves are valid.
        await connection.ExecuteAsync(InsertSql(f.Version, o.ExecutionID, f.PhaseA, f.CategoryA1, f.RuleA1, o.ScoreObservationID, "5, 0, 5, 100, 0"));
        await connection.ExecuteAsync("EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = NULL;");
    }

    // ---------------------------------------------------------------- writes racing finalization

    [Fact]
    public async Task AWriterInFlight_IsSeenByFinalization_WhichWaitsForItsCommit()
    {
        var f = await BuildAsync();
        // A fourth Execution, ended but not yet scored: finalization can only succeed if it sees the writer's observation.
        var e4 = (await NewExecutionAsync(f.Iteration, f.PhaseA, 9)).ExecutionID;
        await EndAsync(e4);

        using var writer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var writerTransaction = writer.BeginTransaction();
        await AcquireAsync(writer, writerTransaction, f.Version);
        await writer.ExecuteAsync(
            "INSERT dbo.ScoreObservation (ExecutionID, ScoreRuleID, RaterID, ObservedScore) VALUES (@e, @r, @rater, 4);",
            new { e = e4, r = f.RuleA1, rater = f.Rater1 }, writerTransaction);

        var finalization = Task.Run(() => FinalizeAsync(f.Version));
        await Task.Delay(500);
        Assert.False(finalization.IsCompleted, "finalization must wait for the in-flight writer (exclusive lock)");

        writerTransaction.Commit();

        var finalized = await finalization.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(finalized.LockedUTC);
        Assert.Equal(7, await SnapshotCountAsync(f.Version)); // the six plus the writer's, committed before the lock
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreObservationSnapshot WHERE ExecutionID = @e;", new { e = e4 }));
    }

    [Fact]
    public async Task WritersBlockedBehindFinalization_AreRejectedAsVersionLocked_OnceItCommits()
    {
        var f = await BuildAsync();
        var newRater = await NewRaterAsync();

        // A finalizer holds the exclusive lock. The controlled-write procedures must be called standalone (Issue #10),
        // so the lock is held directly, exactly as usp_FinalizeExperimentVersion takes it; the version is locked inside
        // that transaction the way finalization locks it (with the token helper from Issue #9).
        using var finalizer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var finalizerTransaction = finalizer.BeginTransaction();
        await AcquireAsync(finalizer, finalizerTransaction, f.Version, exclusive: true);

        var writes = new Func<Task>[]
        {
            () => ObserveAsync(f.E1, f.RuleA2, newRater, 1m),
            () => Recording().UpdateScoreObservationContentAsync(f.Observations[0].ScoreObservationID, new UpdateScoreObservationContentRequest { ObservedScore = 1m }),
            () => NewExecutionAsync(f.Iteration, f.PhaseA, 20),
            () => Design().DeleteParameterAsync(0),
            () => Design().CreateIterationAsync(new CreateIterationRequest { ExperimentVersionID = f.Version, Sequence = 7, IterationLabel = "late", Prompt = "p" }),
        }.Select(w => Task.Run(w)).ToList();

        await Task.Delay(700);
        // The parameter delete targets a missing row, so it fails fast with 404; every other write must be waiting.
        Assert.Equal(4, writes.Count(t => !t.IsCompleted));

        await LockedUtcTestHelper.LockAsync(finalizer, f.Version, finalizerTransaction);
        finalizerTransaction.Commit();

        foreach (var write in writes)
        {
            try
            {
                await write.WaitAsync(TimeSpan.FromSeconds(10));
                throw new Xunit.Sdk.XunitException("a write racing finalization committed");
            }
            catch (NotFoundException)
            {
            }
            catch (SqlException ex)
            {
                Assert.Equal(VersionLocked, ex.Number);
            }
        }

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreObservation WHERE RaterID = @r;", new { r = newRater }));
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Execution WHERE IterationID = @i;", new { i = f.Iteration }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ExperimentVersion WHERE ExperimentVersionID = @v AND LockedUTC IS NOT NULL;", new { v = f.Version }));
    }

    [Fact]
    public async Task ObservationWrites_RacingFinalization_NeverLeaveASnapshotThatMissesAnObservation()
    {
        for (var round = 0; round < 4; round++)
        {
            var f = await BuildAsync();
            var raters = new List<int>();
            for (var n = 0; n < 4; n++)
            {
                raters.Add(await NewRaterAsync());
            }

            var writers = raters.Select(r => Task.Run(async () =>
            {
                try
                {
                    await ObserveAsync(f.E1, f.RuleA2, r, 2m);
                    return true;
                }
                catch (SqlException)
                {
                    return false;
                }
            })).ToList();
            var finalization = Task.Run(async () =>
            {
                try
                {
                    await FinalizeAsync(f.Version);
                    return true;
                }
                catch (SqlException)
                {
                    return false;
                }
            });

            var committed = (await Task.WhenAll(writers)).Count(c => c);
            var finalized = await finalization;

            var observations = await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.ScoreObservation so JOIN dbo.vOwnerExecution o ON o.ExecutionID = so.ExecutionID WHERE o.ExperimentVersionID = @v;", new { v = f.Version });
            Assert.Equal(6 + committed, observations);

            if (finalized)
            {
                // Every observation that exists is in the snapshot; nothing committed after the lock.
                Assert.Equal(observations, await SnapshotCountAsync(f.Version));
                Assert.NotNull(await LockedUtcAsync(f.Version));
            }
            else
            {
                Assert.Equal(0, await SnapshotCountAsync(f.Version));
                Assert.Null(await LockedUtcAsync(f.Version));
            }
        }
    }

    // ---------------------------------------------------------------- the error translation

    [Fact]
    public void FinalizationErrors_ParsesTheMachineReadableSuffix()
    {
        var (detail, extensions) = FinalizationErrors.Parse("2 Execution(s) have no EndUTC: 4,9. [ExecutionIDs=4,9;PhaseIDs=1;Total=2]");

        Assert.Equal("2 Execution(s) have no EndUTC: 4,9.", detail);
        Assert.Equal(new long[] { 4, 9 }, (long[])extensions["executionIDs"]!);
        Assert.Equal(new long[] { 1 }, (long[])extensions["phaseIDs"]!);
        Assert.Equal(2L, extensions["total"]);
    }

    [Fact]
    public void FinalizationErrors_LeavesAMessageWithoutASuffixAlone()
    {
        var (detail, extensions) = FinalizationErrors.Parse("ExperimentVersion 3 has no Executions; there is nothing to finalize.");

        Assert.Equal("ExperimentVersion 3 has no Executions; there is nothing to finalize.", detail);
        Assert.Empty(extensions);
    }
}
