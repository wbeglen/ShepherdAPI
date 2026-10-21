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
/// Issue #4: once any Execution exists for an ExperimentVersion its design (Iterations, Parameters, ScoreRules and the
/// version's Name, VersionLabel, EffectiveDateUTC and Description) is frozen for good, and Executions can never be
/// deleted. Issue #7: ScoreRules follow the same rules with no exception — created, updated and physically deleted only
/// while the version is unlocked and has no Execution. Real SQL Server, through both the API services (controlled-write
/// procedures) and raw SQL (guard triggers).
/// </summary>
[Collection("Database")]
public class DesignFreezeIntegrationTests(DatabaseFixture fixture)
{
    private const int VersionLocked = 50100;
    private const int IdentityChange = 50101;
    private const int DesignFrozen = 50400;
    private const int ExecutionDelete = 50403;

    private static int _sequence = 60000;

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
        new { n = "FreezePhase" + Guid.NewGuid().ToString("N"), s = Interlocked.Increment(ref _sequence) });

    private Task<int> NewCategoryAsync() => ScalarAsync<int>(
        "INSERT dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES (@n, 1);",
        new { n = "FreezeCat" + Guid.NewGuid().ToString("N") });

    private Task<int> NewRaterAsync() => ScalarAsync<int>(
        "INSERT dbo.Rater (Name, RaterType) OUTPUT INSERTED.RaterID VALUES (@n, 'Human');",
        new { n = "FreezeRater" + Guid.NewGuid().ToString("N") });

    private async Task<ExperimentVersion> NewVersionAsync() => await Design().CreateExperimentVersionAsync(new CreateExperimentVersionRequest
    {
        Name = $"Freeze-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
        EffectiveDateUTC = DateTime.UtcNow, Description = "design freeze test",
    });

    private async Task<int> NewRuleAsync(long version, int phase, decimal weight, int? category = null) =>
        (await Design().CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = version, PhaseID = phase, ScoreCategoryID = category ?? await NewCategoryAsync(),
            WeightPercent = weight, MinScore = 0, MaxScore = 5, Active = true,
        })).ScoreRuleID;

    private async Task<Iteration> NewIterationAsync(long version, int sequence) => await Design().CreateIterationAsync(new CreateIterationRequest
    {
        ExperimentVersionID = version, Sequence = sequence, IterationLabel = $"i{sequence}", Prompt = "p",
    });

    private async Task<Parameter> NewParameterAsync(long version, string name) => await Design().CreateParameterAsync(new CreateParameterRequest
    {
        ExperimentVersionID = version, Name = name, Value = "v",
    });

    private Task<Execution> NewExecutionAsync(long iteration, int attempt = 1, int? phase = null) => Recording().CreateExecutionAsync(new CreateExecutionRequest
    {
        IterationID = iteration, AgentID = fixture.AgentId, PhaseID = phase ?? fixture.PhaseId, StatusID = fixture.DraftStatusId,
        StartUTC = DateTime.UtcNow, AttemptNumber = attempt,
    });

    private sealed record Design4(
        long Version, string Name, string VersionLabel, DateTime EffectiveDate,
        long Iteration, long Iteration2, long Parameter, long Parameter2, int Rule, int Rule2, int OtherPhase, int OtherPhaseRule);

    /// <summary>
    /// A complete design with no Execution yet: two Iterations, two Parameters, two ScoreRules in the main phase
    /// (50 + 50) and one in another phase (100).
    /// </summary>
    private async Task<Design4> NewDesignAsync()
    {
        var v = await NewVersionAsync();
        var rule = await NewRuleAsync(v.ExperimentVersionID, fixture.PhaseId, 50m);
        var rule2 = await NewRuleAsync(v.ExperimentVersionID, fixture.PhaseId, 50m);
        var otherPhase = await NewPhaseAsync();
        var otherPhaseRule = await NewRuleAsync(v.ExperimentVersionID, otherPhase, 100m);
        var i1 = await NewIterationAsync(v.ExperimentVersionID, 1);
        var i2 = await NewIterationAsync(v.ExperimentVersionID, 2);
        var p1 = await NewParameterAsync(v.ExperimentVersionID, "p1");
        var p2 = await NewParameterAsync(v.ExperimentVersionID, "p2");
        return new Design4(v.ExperimentVersionID, v.Name, v.VersionLabel, v.EffectiveDateUTC,
            i1.IterationID, i2.IterationID, p1.ParameterID, p2.ParameterID, rule, rule2, otherPhase, otherPhaseRule);
    }

    private static async Task AssertMappedAsync(SqlException ex, int number, string code)
    {
        Assert.Equal(number, ex.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    /// <summary>Runs an API write, expecting the given catalog error, and verifies its HTTP mapping.</summary>
    private static async Task FrozenAsync(Func<Task> write, string because)
    {
        SqlException ex;
        try
        {
            await write();
            throw new Xunit.Sdk.XunitException($"{because}: expected design-frozen, but the write succeeded.");
        }
        catch (SqlException e)
        {
            ex = e;
        }

        try
        {
            await AssertMappedAsync(ex, DesignFrozen, "design-frozen");
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            throw new Xunit.Sdk.XunitException($"{because}: {e.Message}");
        }
    }

    private static async Task AcquireAsync(IDbConnection connection, IDbTransaction transaction, long versionId, bool exclusive = false)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", versionId);
        parameters.Add("@Exclusive", exclusive);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_AcquireVersionLock", parameters, transaction, commandType: CommandType.StoredProcedure);
    }

    private Task LockAsync(long version) => LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, version);

    private async Task<bool> RuleExistsAsync(int rule) =>
        await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreRule WHERE ScoreRuleID = @rule;", new { rule }) == 1;

    private Task<decimal> RuleWeightAsync(int rule) =>
        ScalarAsync<decimal>("SELECT WeightPercent FROM dbo.ScoreRule WHERE ScoreRuleID = @rule;", new { rule });

    // ---------------------------------------------------------------- deployment

    [Fact]
    public async Task DesignFreezeObjects_AreDeployed()
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var procs = (await connection.QueryAsync<string>("SELECT name FROM sys.procedures;")).ToHashSet();
        foreach (var expected in new[]
        {
            "usp_AssertDesignNotFrozen", "usp_AssertDesignWritable", "usp_UpdateExperimentVersion",
            "usp_CreateIteration", "usp_UpdateIteration", "usp_DeleteIteration",
            "usp_CreateParameter", "usp_UpdateParameter", "usp_DeleteParameter",
            "usp_CreateScoreRule", "usp_UpdateScoreRule", "usp_DeleteScoreRule",
        })
        {
            Assert.Contains(expected, procs);
        }

        // Issue #7: the soft-delete procedure is gone, also from a database it was deployed to before.
        Assert.DoesNotContain("usp_SoftDeleteScoreRule", procs);

        Assert.NotNull(await connection.QuerySingleAsync<int?>("SELECT OBJECT_ID(N'dbo.fn_VersionHasExecutions');"));
    }

    [Fact]
    public async Task Scripts_AreRerunnable()
    {
        await fixture.ApplyDeploymentScriptsAsync();
        await fixture.ApplyDeploymentScriptsAsync();

        var d = await NewDesignAsync();
        await NewExecutionAsync(d.Iteration);
        await FrozenAsync(() => Design().DeleteParameterAsync(d.Parameter), "after re-applying the scripts");
    }

    // ---------------------------------------------------------------- before the first Execution

    [Fact]
    public async Task EveryListedWrite_Succeeds_BeforeTheFirstExecution()
    {
        var d = await NewDesignAsync();
        var design = Design();

        var updated = await design.UpdateExperimentVersionAsync(d.Version, new UpdateExperimentVersionRequest
        {
            Name = d.Name, VersionLabel = d.VersionLabel + "b", EffectiveDateUTC = d.EffectiveDate.AddDays(1), Description = "edited",
        });
        Assert.Equal("edited", updated.Description);

        var iteration = await design.CreateIterationAsync(new CreateIterationRequest { ExperimentVersionID = d.Version, Sequence = 3, IterationLabel = "i3", Prompt = "p" });
        var revised = await design.UpdateIterationAsync(iteration.IterationID, new UpdateIterationRequest { Sequence = 4, IterationLabel = "i4", Prompt = "p2" });
        Assert.Equal(4, revised.Sequence);
        await design.DeleteIterationAsync(iteration.IterationID);
        await Assert.ThrowsAsync<NotFoundException>(() => design.GetIterationAsync(iteration.IterationID));

        var parameter = await design.CreateParameterAsync(new CreateParameterRequest { ExperimentVersionID = d.Version, Name = "p3", Value = "v" });
        var revisedParameter = await design.UpdateParameterAsync(parameter.ParameterID, new UpdateParameterRequest { Value = "v2", Unit = "u" });
        Assert.Equal("v2", revisedParameter.Value);
        Assert.Equal("p3", revisedParameter.Name);
        await design.DeleteParameterAsync(parameter.ParameterID);

        var rule = await design.CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = d.Version, PhaseID = d.OtherPhase, ScoreCategoryID = await NewCategoryAsync(),
            WeightPercent = 1m, MinScore = 0, MaxScore = 5, Active = true,
        });
        var revisedRule = await design.UpdateScoreRuleAsync(rule.ScoreRuleID, new UpdateScoreRuleRequest { WeightPercent = 2m, MinScore = 0, MaxScore = 7, Active = true });
        Assert.Equal((2m, 7), (revisedRule.WeightPercent, revisedRule.MaxScore));
        await design.DeleteScoreRuleAsync(rule.ScoreRuleID);
        Assert.False(await RuleExistsAsync(rule.ScoreRuleID));
    }

    [Fact]
    public async Task WritesToAMissingTarget_AreStillNotFound()
    {
        var design = Design();
        await Assert.ThrowsAsync<NotFoundException>(() => design.UpdateExperimentVersionAsync(long.MaxValue, new UpdateExperimentVersionRequest { Name = "x", VersionLabel = "v", EffectiveDateUTC = DateTime.UtcNow, Description = "d" }));
        await Assert.ThrowsAsync<NotFoundException>(() => design.CreateIterationAsync(new CreateIterationRequest { ExperimentVersionID = long.MaxValue, Sequence = 1, IterationLabel = "i", Prompt = "p" }));
        await Assert.ThrowsAsync<NotFoundException>(() => design.CreateParameterAsync(new CreateParameterRequest { ExperimentVersionID = long.MaxValue, Name = "p" }));
        await Assert.ThrowsAsync<NotFoundException>(() => design.CreateScoreRuleAsync(new CreateScoreRuleRequest { ExperimentVersionID = long.MaxValue, PhaseID = fixture.PhaseId, ScoreCategoryID = fixture.ScoreCategoryId, WeightPercent = 1, MinScore = 0, MaxScore = 5 }));
        await Assert.ThrowsAsync<NotFoundException>(() => design.UpdateIterationAsync(long.MaxValue, new UpdateIterationRequest { Sequence = 1, IterationLabel = "i", Prompt = "p" }));
        await Assert.ThrowsAsync<NotFoundException>(() => design.DeleteIterationAsync(long.MaxValue));
        await Assert.ThrowsAsync<NotFoundException>(() => design.UpdateParameterAsync(long.MaxValue, new UpdateParameterRequest { Value = "v" }));
        await Assert.ThrowsAsync<NotFoundException>(() => design.DeleteParameterAsync(long.MaxValue));
        await Assert.ThrowsAsync<NotFoundException>(() => design.UpdateScoreRuleAsync(int.MaxValue, new UpdateScoreRuleRequest { WeightPercent = 1, MinScore = 0, MaxScore = 5 }));
        await Assert.ThrowsAsync<NotFoundException>(() => design.DeleteScoreRuleAsync(int.MaxValue));
    }

    // ---------------------------------------------------------------- after the first Execution

    private async Task AssertEveryDesignWriteFrozenAsync(Design4 d, string because)
    {
        var design = Design();

        await FrozenAsync(() => design.UpdateExperimentVersionAsync(d.Version, new UpdateExperimentVersionRequest
            { Name = d.Name + "x", VersionLabel = d.VersionLabel, EffectiveDateUTC = d.EffectiveDate, Description = "d" }), $"{because}: version Name");
        await FrozenAsync(() => design.UpdateExperimentVersionAsync(d.Version, new UpdateExperimentVersionRequest
            { Name = d.Name, VersionLabel = d.VersionLabel + "x", EffectiveDateUTC = d.EffectiveDate, Description = "d" }), $"{because}: version VersionLabel");
        await FrozenAsync(() => design.UpdateExperimentVersionAsync(d.Version, new UpdateExperimentVersionRequest
            { Name = d.Name, VersionLabel = d.VersionLabel, EffectiveDateUTC = d.EffectiveDate.AddDays(1), Description = "d" }), $"{because}: version EffectiveDateUTC");
        await FrozenAsync(() => design.UpdateExperimentVersionAsync(d.Version, new UpdateExperimentVersionRequest
            { Name = d.Name, VersionLabel = d.VersionLabel, EffectiveDateUTC = d.EffectiveDate, Description = "different" }), $"{because}: version Description");

        await FrozenAsync(() => design.CreateIterationAsync(new CreateIterationRequest { ExperimentVersionID = d.Version, Sequence = 9, IterationLabel = "i9", Prompt = "p" }), $"{because}: iteration create");
        await FrozenAsync(() => design.UpdateIterationAsync(d.Iteration2, new UpdateIterationRequest { Sequence = 2, IterationLabel = "changed", Prompt = "p" }), $"{because}: iteration update");
        await FrozenAsync(() => design.DeleteIterationAsync(d.Iteration2), $"{because}: iteration delete");

        await FrozenAsync(() => design.CreateParameterAsync(new CreateParameterRequest { ExperimentVersionID = d.Version, Name = "p9" }), $"{because}: parameter create");
        await FrozenAsync(() => design.UpdateParameterAsync(d.Parameter2, new UpdateParameterRequest { Value = "changed" }), $"{because}: parameter update");
        await FrozenAsync(() => design.DeleteParameterAsync(d.Parameter2), $"{because}: parameter delete");

        await FrozenAsync(async () => await design.CreateScoreRuleAsync(new CreateScoreRuleRequest
            { ExperimentVersionID = d.Version, PhaseID = fixture.PhaseId, ScoreCategoryID = fixture.ScoreCategoryId + 1000, WeightPercent = 1, MinScore = 0, MaxScore = 5 }), $"{because}: score rule create");
        await FrozenAsync(() => design.UpdateScoreRuleAsync(d.Rule2, new UpdateScoreRuleRequest { WeightPercent = 1, MinScore = 0, MaxScore = 5 }), $"{because}: score rule update");
        await FrozenAsync(() => design.DeleteScoreRuleAsync(d.Rule2), $"{because}: score rule delete");
        await FrozenAsync(() => design.DeleteScoreRuleAsync(d.OtherPhaseRule), $"{because}: score rule delete (another phase)");

        // Nothing changed.
        Assert.Equal(50m, await RuleWeightAsync(d.Rule2));
        Assert.True(await RuleExistsAsync(d.OtherPhaseRule));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Iteration WHERE ExperimentVersionID = @v;", new { v = d.Version }));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE ExperimentVersionID = @v;", new { v = d.Version }));
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreRule WHERE ExperimentVersionID = @v;", new { v = d.Version }));
        var version = await Design().GetExperimentVersionAsync(d.Version);
        Assert.Equal((d.Name, d.VersionLabel, d.EffectiveDate), (version.Name, version.VersionLabel, version.EffectiveDateUTC));
    }

    [Fact]
    public async Task EveryListedWrite_IsRejectedWithDesignFrozen_OnceAnyExecutionExists()
    {
        var d = await NewDesignAsync();
        await NewExecutionAsync(d.Iteration);

        await AssertEveryDesignWriteFrozenAsync(d, "after the first Execution");
    }

    [Fact]
    public async Task TheFreeze_IsDistinctFromTheLockedVersionError()
    {
        var d = await NewDesignAsync();
        await NewExecutionAsync(d.Iteration);

        var frozen = await Assert.ThrowsAsync<SqlException>(() => Design().DeleteParameterAsync(d.Parameter));
        await AssertMappedAsync(frozen, DesignFrozen, "design-frozen");

        await LockAsync(d.Version);
        var locked = await Assert.ThrowsAsync<SqlException>(() => Design().DeleteParameterAsync(d.Parameter));
        await AssertMappedAsync(locked, VersionLocked, "version-locked");

        Assert.NotEqual(ErrorCatalog.DesignFrozen.Code, ErrorCatalog.VersionLocked.Code);
    }

    [Fact]
    public async Task TheFreeze_HoldsInAnotherPhaseOrForACompletedExecutionOrOnceObservationsExist()
    {
        // An Execution in a phase other than the one being edited freezes everything.
        var otherPhase = await NewDesignAsync();
        await NewExecutionAsync(otherPhase.Iteration, phase: otherPhase.OtherPhase);
        await AssertEveryDesignWriteFrozenAsync(otherPhase, "Execution in another phase");

        // A completed Execution (EndUTC set) freezes it too, and so does an observation on it.
        var completed = await NewDesignAsync();
        var execution = await NewExecutionAsync(completed.Iteration);
        await Recording().UpdateExecutionRunFactsAsync(execution.ExecutionID, new UpdateExecutionRunFactsRequest { EndUTC = DateTime.UtcNow });
        await AssertEveryDesignWriteFrozenAsync(completed, "completed Execution");

        var observed = await NewDesignAsync();
        var observedExecution = await NewExecutionAsync(observed.Iteration);
        await Recording().CreateScoreObservationAsync(new CreateScoreObservationRequest
            { ExecutionID = observedExecution.ExecutionID, ScoreRuleID = observed.Rule, RaterID = fixture.RaterId, ObservedScore = 3m });
        await AssertEveryDesignWriteFrozenAsync(observed, "Execution with an observation");

        // A bare Execution with no observation and no EndUTC freezes it as well (the first case above).
    }

    [Fact]
    public async Task TheFreeze_IsPerVersion_AnotherVersionStaysEditable()
    {
        var frozen = await NewDesignAsync();
        await NewExecutionAsync(frozen.Iteration);
        var free = await NewDesignAsync();

        await Design().DeleteParameterAsync(free.Parameter);
        await Design().DeleteScoreRuleAsync(free.Rule2);
        await FrozenAsync(() => Design().DeleteParameterAsync(frozen.Parameter), "the frozen version");
    }

    // ---------------------------------------------------------------- Executions can never be deleted

    [Fact]
    public async Task AnExecutionCannotBeDeleted_AndTheDesignStaysFrozen()
    {
        var d = await NewDesignAsync();
        var execution = await NewExecutionAsync(d.Iteration);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("DELETE dbo.Execution WHERE ExecutionID = @id;", new { id = execution.ExecutionID }));
        await AssertMappedAsync(ex, ExecutionDelete, "execution-delete-rejected");

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Execution WHERE ExecutionID = @id;", new { id = execution.ExecutionID }));
        await FrozenAsync(() => Design().DeleteParameterAsync(d.Parameter), "after the delete attempt");
    }

    [Fact]
    public async Task ExecutionDeletes_AreRejected_WhateverTheVersionState_AndMultirowDeletesFailWhole()
    {
        var d = await NewDesignAsync();
        var first = await NewExecutionAsync(d.Iteration, 1);
        var second = await NewExecutionAsync(d.Iteration, 2);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var multirow = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE dbo.Execution WHERE ExecutionID IN (@a, @b);", new { a = first.ExecutionID, b = second.ExecutionID }));
        Assert.Equal(ExecutionDelete, multirow.Number);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Execution WHERE IterationID = @i;", new { i = d.Iteration }));

        // Locked version: still rejected, and as an Execution delete rather than as a locked version.
        await LockAsync(d.Version);
        var locked = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("DELETE dbo.Execution WHERE ExecutionID = @id;", new { id = first.ExecutionID }));
        Assert.Equal(ExecutionDelete, locked.Number);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Execution WHERE IterationID = @i;", new { i = d.Iteration }));
    }

    [Fact]
    public async Task AnExecutionDelete_InsideATransaction_RollsEverythingBack()
    {
        var d = await NewDesignAsync();
        var execution = await NewExecutionAsync(d.Iteration);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            BEGIN TRANSACTION;
            UPDATE dbo.Execution SET OutcomeNotes = N'touched' WHERE ExecutionID = @id;
            DELETE dbo.Execution WHERE ExecutionID = @id;
            COMMIT;
            """, new { id = execution.ExecutionID }));

        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
        Assert.Null(await ScalarAsync<string?>("SELECT OutcomeNotes FROM dbo.Execution WHERE ExecutionID = @id;", new { id = execution.ExecutionID }));
    }

    // ---------------------------------------------------------------- the database guards, bypassing the API

    [Fact]
    public async Task TheGuardTriggers_RejectRawWrites_OnceAnExecutionExists()
    {
        var d = await NewDesignAsync();
        await NewExecutionAsync(d.Iteration);
        var category = await NewCategoryAsync();

        var statements = new (string Label, string Sql)[]
        {
            ("Iteration insert", $"INSERT dbo.Iteration (ExperimentVersionID, Sequence, IterationLabel, Prompt) VALUES ({d.Version}, 90, N'x', N'x');"),
            ("Iteration update", $"UPDATE dbo.Iteration SET Notes = N'x' WHERE IterationID = {d.Iteration2};"),
            ("Iteration delete", $"DELETE dbo.Iteration WHERE IterationID = {d.Iteration2};"),
            ("Parameter insert", $"INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({d.Version}, N'raw');"),
            ("Parameter update", $"UPDATE dbo.Parameter SET Value = N'x' WHERE ParameterID = {d.Parameter};"),
            ("Parameter delete", $"DELETE dbo.Parameter WHERE ParameterID = {d.Parameter2};"),
            ("ScoreRule insert", $"INSERT dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active) VALUES ({d.Version}, {fixture.PhaseId}, {category}, 1, 0, 5, 1);"),
            ("ScoreRule update", $"UPDATE dbo.ScoreRule SET WeightPercent = 1 WHERE ScoreRuleID = {d.Rule};"),
            ("ScoreRule delete", $"DELETE dbo.ScoreRule WHERE ScoreRuleID = {d.OtherPhaseRule};"),
            ("ExperimentVersion Name", $"UPDATE dbo.ExperimentVersion SET Name = N'renamed' WHERE ExperimentVersionID = {d.Version};"),
            ("ExperimentVersion VersionLabel", $"UPDATE dbo.ExperimentVersion SET VersionLabel = N'v9' WHERE ExperimentVersionID = {d.Version};"),
            ("ExperimentVersion EffectiveDateUTC", $"UPDATE dbo.ExperimentVersion SET EffectiveDateUTC = DATEADD(DAY, 1, EffectiveDateUTC) WHERE ExperimentVersionID = {d.Version};"),
            ("ExperimentVersion Description", $"UPDATE dbo.ExperimentVersion SET Description = N'changed' WHERE ExperimentVersionID = {d.Version};"),
        };

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        foreach (var (label, sql) in statements)
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
            Assert.True(ex.Number == DesignFrozen, $"{label}: expected {DesignFrozen} but got {ex.Number} ({ex.Message})");
        }

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Iteration WHERE ExperimentVersionID = @v;", new { v = d.Version }));
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreRule WHERE ExperimentVersionID = @v;", new { v = d.Version }));
        Assert.Equal(50m, await RuleWeightAsync(d.Rule));
    }

    [Fact]
    public async Task MultirowStatements_MixingFrozenAndUnfrozenVersions_AreRejectedWhole()
    {
        var frozen = await NewDesignAsync();
        await NewExecutionAsync(frozen.Iteration);
        var free = await NewDesignAsync();

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();

        var insert = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            $"INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({free.Version}, N'mixFree'), ({frozen.Version}, N'mixFrozen');"));
        Assert.Equal(DesignFrozen, insert.Number);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE Name IN (N'mixFree', N'mixFrozen');"));

        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            $"UPDATE dbo.Parameter SET Notes = N'mixed-freeze' WHERE ParameterID IN ({free.Parameter}, {frozen.Parameter});"));
        Assert.Equal(DesignFrozen, update.Number);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE Notes = N'mixed-freeze';"));

        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            $"DELETE dbo.Parameter WHERE ParameterID IN ({free.Parameter2}, {frozen.Parameter2});"));
        Assert.Equal(DesignFrozen, delete.Number);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE ParameterID = @p;", new { p = free.Parameter2 }));

        var rules = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            $"UPDATE dbo.ScoreRule SET WeightPercent = 1 WHERE ScoreRuleID IN ({free.Rule2}, {frozen.Rule2});"));
        Assert.Equal(DesignFrozen, rules.Number);
        Assert.Equal(50m, await RuleWeightAsync(free.Rule2));

        var ruleDelete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            $"DELETE dbo.ScoreRule WHERE ScoreRuleID IN ({free.Rule2}, {frozen.Rule2});"));
        Assert.Equal(DesignFrozen, ruleDelete.Number);
        Assert.True(await RuleExistsAsync(free.Rule2));

        var versions = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            $"UPDATE dbo.ExperimentVersion SET Description = N'mixed' WHERE ExperimentVersionID IN ({free.Version}, {frozen.Version});"));
        Assert.Equal(DesignFrozen, versions.Number);
        Assert.NotEqual("mixed", await ScalarAsync<string>("SELECT Description FROM dbo.ExperimentVersion WHERE ExperimentVersionID = @v;", new { v = free.Version }));

        // Without a frozen participant the same statement shapes work.
        var marker = Guid.NewGuid().ToString("N"); // the database is shared across test classes, so keep the marker unique
        await ExecAsync($"UPDATE dbo.Parameter SET Notes = N'{marker}' WHERE ParameterID IN ({free.Parameter}, {free.Parameter2});");
        Assert.Equal(2, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Parameter WHERE Notes = N'{marker}';"));
    }

    [Fact]
    public async Task ARow_CannotBeMovedBetweenVersions_ToEvadeTheFreeze()
    {
        var frozen = await NewDesignAsync();
        await NewExecutionAsync(frozen.Iteration);
        var free = await NewDesignAsync();

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var statements = new[]
        {
            $"UPDATE dbo.Iteration SET ExperimentVersionID = {free.Version}, Sequence = 99 WHERE IterationID = {frozen.Iteration2};",
            $"UPDATE dbo.Parameter SET ExperimentVersionID = {free.Version}, Name = N'moved' WHERE ParameterID = {frozen.Parameter2};",
            $"UPDATE dbo.ScoreRule SET ExperimentVersionID = {free.Version} WHERE ScoreRuleID = {frozen.Rule2};",
            // ...and into the frozen version from outside.
            $"UPDATE dbo.Iteration SET ExperimentVersionID = {frozen.Version}, Sequence = 98 WHERE IterationID = {free.Iteration2};",
            $"UPDATE dbo.Parameter SET ExperimentVersionID = {frozen.Version}, Name = N'moved-in' WHERE ParameterID = {free.Parameter2};",
        };
        foreach (var sql in statements)
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
            Assert.True(ex.Number == IdentityChange, $"expected {IdentityChange} but got {ex.Number} ({ex.Message}) for: {sql}");
        }

        Assert.Equal(frozen.Version, await ScalarAsync<long>("SELECT ExperimentVersionID FROM dbo.Iteration WHERE IterationID = @i;", new { i = frozen.Iteration2 }));
        Assert.Equal(free.Version, await ScalarAsync<long>("SELECT ExperimentVersionID FROM dbo.Parameter WHERE ParameterID = @p;", new { p = free.Parameter2 }));
    }

    // ---------------------------------------------------------------- what the freeze leaves alone

    [Fact]
    public async Task StatusExecutionsArtifactsObservationsAndEvidence_AreUnaffectedByTheFreeze()
    {
        var d = await NewDesignAsync();
        var execution = await NewExecutionAsync(d.Iteration);
        var recording = Recording();

        // StatusID: still writable, with the dedicated operation and with raw SQL.
        var retired = await Design().SetExperimentVersionStatusAsync(d.Version, new SetExperimentVersionStatusRequest { StatusID = fixture.RetiredStatusId });
        Assert.Equal(fixture.RetiredStatusId, retired.StatusID);
        await ExecAsync("UPDATE dbo.ExperimentVersion SET StatusID = @s WHERE ExperimentVersionID = @v;", new { s = fixture.DraftStatusId, v = d.Version });

        // A version-level update that changes only StatusID-adjacent state does not trip the freeze either: an
        // identical-value descriptive update is a no-op, not a change.
        await ExecAsync("UPDATE dbo.ExperimentVersion SET Name = Name, Description = Description WHERE ExperimentVersionID = @v;", new { v = d.Version });

        // More Executions (in either phase), run facts, observations, artifacts and evidence links.
        var second = await NewExecutionAsync(d.Iteration, 2);
        await NewExecutionAsync(d.Iteration2, 1, d.OtherPhase);
        var updatedFacts = await recording.UpdateExecutionRunFactsAsync(second.ExecutionID, new UpdateExecutionRunFactsRequest { OutcomeNotes = "notes" });
        Assert.Equal("notes", updatedFacts.OutcomeNotes);

        var observation = await recording.CreateScoreObservationAsync(new CreateScoreObservationRequest
            { ExecutionID = execution.ExecutionID, ScoreRuleID = d.Rule, RaterID = fixture.RaterId, ObservedScore = 3m });
        await recording.UpdateScoreObservationContentAsync(observation.ScoreObservationID, new UpdateScoreObservationContentRequest { ObservedScore = 4m });
        var artifact = await recording.CreateArtifactAsync(new CreateArtifactRequest
        {
            ExecutionID = execution.ExecutionID, ArtifactTypeID = fixture.ArtifactTypeId, OriginalName = "e.png", OriginalURI = "file://e",
            StoredURI = "file://e", MimeType = "image/png", ByteLength = 10, SHA256 = new string('C', 64), ReceivedUTC = DateTime.UtcNow,
        });
        await recording.AddEvidenceAsync(observation.ScoreObservationID, new CreateScoreObservationEvidenceRequest { ArtifactID = artifact.ArtifactID });
        Assert.Equal(4m, (await recording.GetScoreObservationAsync(observation.ScoreObservationID)).ObservedScore);
    }

    [Fact]
    public async Task ReadsAreUnaffected_AndARuleDeletedBeforeTheFirstExecutionIsGone()
    {
        var d = await NewDesignAsync();
        var spare = await NewRuleAsync(d.Version, d.OtherPhase, 0m);
        await Design().DeleteScoreRuleAsync(spare);
        await NewExecutionAsync(d.Iteration);

        var rules = await Design().GetScoreRulesAsync(d.Version);
        Assert.DoesNotContain(rules, r => r.ScoreRuleID == spare);
        Assert.Equal([d.Rule, d.Rule2, d.OtherPhaseRule], rules.Select(r => r.ScoreRuleID).Order());
        await Assert.ThrowsAsync<NotFoundException>(() => Design().GetScoreRuleAsync(spare));
        Assert.Equal(2, (await Design().GetIterationsAsync(d.Version)).Count);
        Assert.Equal(2, (await Design().GetParametersAsync(d.Version)).Count);
    }

    // ---------------------------------------------------------------- ScoreRule update and physical delete (#7)

    [Fact]
    public async Task UpdateScoreRule_ChangesOnlyTheNonKeyFields_AndKeepsTheRuleIdentity()
    {
        var d = await NewDesignAsync();
        var before = await Design().GetScoreRuleAsync(d.Rule2);

        var after = await Design().UpdateScoreRuleAsync(d.Rule2, new UpdateScoreRuleRequest
            { WeightPercent = 40m, MinScore = 1, MaxScore = 10, Active = false, RuleNotes = "revised" });

        Assert.Equal((before.ScoreRuleID, before.ExperimentVersionID, before.PhaseID, before.ScoreCategoryID),
                     (after.ScoreRuleID, after.ExperimentVersionID, after.PhaseID, after.ScoreCategoryID));
        Assert.Equal((40m, 1, 10, false, "revised"), (after.WeightPercent, after.MinScore, after.MaxScore, after.Active, after.RuleNotes));
    }

    [Fact]
    public async Task UpdateScoreRule_RejectsAnInvalidRange_AndAMissingRule()
    {
        var d = await NewDesignAsync();

        await Assert.ThrowsAsync<ValidationAppException>(() => Design().UpdateScoreRuleAsync(d.Rule2, new UpdateScoreRuleRequest
            { WeightPercent = 50m, MinScore = 5, MaxScore = 5 }));
        await Assert.ThrowsAsync<NotFoundException>(() => Design().UpdateScoreRuleAsync(int.MaxValue, new UpdateScoreRuleRequest
            { WeightPercent = 50m, MinScore = 0, MaxScore = 5 }));

        Assert.Equal(50m, await RuleWeightAsync(d.Rule2));
    }

    [Fact]
    public async Task DeleteScoreRule_RemovesTheRow_AndADeletedRuleIsThenNotFound()
    {
        var d = await NewDesignAsync();

        await Design().DeleteScoreRuleAsync(d.Rule2);

        Assert.False(await RuleExistsAsync(d.Rule2));
        Assert.True(await RuleExistsAsync(d.Rule));
        await Assert.ThrowsAsync<NotFoundException>(() => Design().GetScoreRuleAsync(d.Rule2));
        await Assert.ThrowsAsync<NotFoundException>(() => Design().DeleteScoreRuleAsync(d.Rule2));
        await Assert.ThrowsAsync<NotFoundException>(() => Design().UpdateScoreRuleAsync(d.Rule2, new UpdateScoreRuleRequest
            { WeightPercent = 50m, MinScore = 0, MaxScore = 5 }));
    }

    [Fact]
    public async Task DeleteScoreRule_FreesItsVersionPhaseCategoryCombination()
    {
        var d = await NewDesignAsync();
        var category = (await Design().GetScoreRuleAsync(d.Rule2)).ScoreCategoryID;

        // While the rule exists the combination is taken.
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => NewRuleAsync(d.Version, fixture.PhaseId, 50m, category));
        Assert.Equal(2627, duplicate.Number);

        await Design().DeleteScoreRuleAsync(d.Rule2);
        var recreated = await NewRuleAsync(d.Version, fixture.PhaseId, 50m, category);

        Assert.NotEqual(d.Rule2, recreated);
        Assert.Equal(category, (await Design().GetScoreRuleAsync(recreated)).ScoreCategoryID);

        // The recreated rule makes the phase total 100.00 again, so the first Execution is accepted.
        await NewExecutionAsync(d.Iteration);
    }

    [Fact]
    public async Task OnALockedVersion_EveryScoreRuleWriteIsVersionLocked_WithOrWithoutExecutions()
    {
        // A finalized-style lock (with an Execution), and a lock set on a version that never had one: both are final.
        var withExecution = await NewDesignAsync();
        await NewExecutionAsync(withExecution.Iteration);
        await LockAsync(withExecution.Version);
        var withoutExecution = await NewDesignAsync();
        await LockAsync(withoutExecution.Version);

        foreach (var d in new[] { withExecution, withoutExecution })
        {
            var create = await Assert.ThrowsAsync<SqlException>(() => NewRuleAsync(d.Version, d.OtherPhase, 1m));
            await AssertMappedAsync(create, VersionLocked, "version-locked");
            var update = await Assert.ThrowsAsync<SqlException>(() => Design().UpdateScoreRuleAsync(d.Rule2, new UpdateScoreRuleRequest
                { WeightPercent = 1m, MinScore = 0, MaxScore = 5 }));
            await AssertMappedAsync(update, VersionLocked, "version-locked");
            var delete = await Assert.ThrowsAsync<SqlException>(() => Design().DeleteScoreRuleAsync(d.Rule2));
            await AssertMappedAsync(delete, VersionLocked, "version-locked");

            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            foreach (var sql in new[]
            {
                $"UPDATE dbo.ScoreRule SET WeightPercent = 1 WHERE ScoreRuleID = {d.Rule};",
                $"UPDATE dbo.ScoreRule SET RuleNotes = N'extra' WHERE ScoreRuleID = {d.Rule};",
                $"DELETE dbo.ScoreRule WHERE ScoreRuleID = {d.Rule};",
            })
            {
                var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
                Assert.True(ex.Number == VersionLocked, $"expected {VersionLocked} but got {ex.Number} for: {sql}");
            }

            Assert.True(await RuleExistsAsync(d.Rule));
            Assert.True(await RuleExistsAsync(d.Rule2));
            Assert.Equal(50m, await RuleWeightAsync(d.Rule));
        }
    }

    [Fact]
    public async Task Status_NeitherAuthorizesNorBlocksScoreRuleWrites()
    {
        // A writable version whose StatusID says Locked or Retired still accepts every rule write...
        var writable = await NewDesignAsync();
        foreach (var status in new[] { fixture.LockedStatusId, fixture.RetiredStatusId })
        {
            await Design().SetExperimentVersionStatusAsync(writable.Version, new SetExperimentVersionStatusRequest { StatusID = status });
            var rule = await NewRuleAsync(writable.Version, writable.OtherPhase, 0m);
            await Design().UpdateScoreRuleAsync(rule, new UpdateScoreRuleRequest { WeightPercent = 0m, MinScore = 0, MaxScore = 9 });
            await Design().DeleteScoreRuleAsync(rule);
        }

        // ...and a frozen or locked version whose StatusID says Draft still refuses them.
        var frozen = await NewDesignAsync();
        await NewExecutionAsync(frozen.Iteration);
        await Design().SetExperimentVersionStatusAsync(frozen.Version, new SetExperimentVersionStatusRequest { StatusID = fixture.DraftStatusId });
        await FrozenAsync(() => Design().DeleteScoreRuleAsync(frozen.Rule2), "frozen version with Draft status");

        var locked = await NewDesignAsync();
        await LockAsync(locked.Version);
        await Design().SetExperimentVersionStatusAsync(locked.Version, new SetExperimentVersionStatusRequest { StatusID = fixture.DraftStatusId });
        var ex = await Assert.ThrowsAsync<SqlException>(() => Design().DeleteScoreRuleAsync(locked.Rule2));
        await AssertMappedAsync(ex, VersionLocked, "version-locked");
    }

    [Fact]
    public async Task TheForeignKeys_StillRefuseDeletingAReferencedRule_EvenOnAWritableVersion()
    {
        // The observation procedure never lets an observation use another version's rule, but a raw insert can
        // (the ScoreObservation trigger guards only the observation's own version). Such a reference still keeps
        // the rule: the foreign key refuses the delete before the guard trigger is reached.
        var free = await NewDesignAsync();
        var live = await NewDesignAsync();
        var execution = await NewExecutionAsync(live.Iteration);
        await ExecAsync(
            "INSERT dbo.ScoreObservation (ExecutionID, ScoreRuleID, RaterID, ObservedScore) VALUES (@e, @r, @rater, 1);",
            new { e = execution.ExecutionID, r = free.Rule2, rater = await NewRaterAsync() });

        var api = await Assert.ThrowsAsync<SqlException>(() => Design().DeleteScoreRuleAsync(free.Rule2));
        Assert.Equal(547, api.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(api);
        Assert.Equal(409, status);
        Assert.Equal(ErrorCatalog.ConstraintViolation.Code, body.GetProperty("code").GetString());

        var raw = await Assert.ThrowsAsync<SqlException>(() => ExecAsync($"DELETE dbo.ScoreRule WHERE ScoreRuleID = {free.Rule2};"));
        Assert.Equal(547, raw.Number);

        Assert.True(await RuleExistsAsync(free.Rule2));
    }

    // ---------------------------------------------------------------- serialization with Execution creation (#1)

    [Fact]
    public async Task ADesignWrite_RacingTheFirstExecution_IsRejected_IfTheExecutionCommitsFirst()
    {
        var d = await NewDesignAsync();

        // The first Execution is being created and has not committed yet: it holds the version's update lock.
        using var creator = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var creatorTransaction = creator.BeginTransaction();
        await AcquireAsync(creator, creatorTransaction, d.Version);
        await creator.ExecuteAsync(
            "INSERT dbo.Execution (IterationID, AgentID, PhaseID, StatusID, StartUTC, InterventionCount, AttemptNumber) VALUES (@i, @a, @p, @s, SYSUTCDATETIME(), 0, 1);",
            new { i = d.Iteration, a = fixture.AgentId, p = fixture.PhaseId, s = fixture.DraftStatusId }, creatorTransaction);

        var writes = new Func<Task>[]
        {
            () => Design().DeleteParameterAsync(d.Parameter),
            () => Design().CreateIterationAsync(new CreateIterationRequest { ExperimentVersionID = d.Version, Sequence = 9, IterationLabel = "i9", Prompt = "p" }),
            () => Design().UpdateExperimentVersionAsync(d.Version, new UpdateExperimentVersionRequest { Name = d.Name, VersionLabel = d.VersionLabel, EffectiveDateUTC = d.EffectiveDate, Description = "raced" }),
            () => Design().UpdateScoreRuleAsync(d.Rule, new UpdateScoreRuleRequest { WeightPercent = 1, MinScore = 0, MaxScore = 5 }),
            () => Design().DeleteScoreRuleAsync(d.Rule2),
        };
        var racing = writes.Select(w => Task.Run(w)).ToList();
        await Task.Delay(500);
        Assert.All(racing, t => Assert.False(t.IsCompleted, "a design write must wait for the in-flight Execution creation"));

        creatorTransaction.Commit();

        foreach (var race in racing)
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => race.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(DesignFrozen, ex.Number);
        }

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE ExperimentVersionID = @v;", new { v = d.Version }));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Iteration WHERE ExperimentVersionID = @v;", new { v = d.Version }));
        Assert.True(await RuleExistsAsync(d.Rule2));
        Assert.Equal(50m, await RuleWeightAsync(d.Rule));
    }

    [Fact]
    public async Task ARawDesignWrite_RacingTheFirstExecution_IsAlsoRejected_ByTheGuardTrigger()
    {
        var d = await NewDesignAsync();

        using var creator = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var creatorTransaction = creator.BeginTransaction();
        await AcquireAsync(creator, creatorTransaction, d.Version);
        await creator.ExecuteAsync(
            "INSERT dbo.Execution (IterationID, AgentID, PhaseID, StatusID, StartUTC, InterventionCount, AttemptNumber) VALUES (@i, @a, @p, @s, SYSUTCDATETIME(), 0, 1);",
            new { i = d.Iteration, a = fixture.AgentId, p = fixture.PhaseId, s = fixture.DraftStatusId }, creatorTransaction);

        var raw = Task.Run(async () =>
        {
            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync($"INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({d.Version}, N'raw-racer');");
        });
        await Task.Delay(500);
        Assert.False(raw.IsCompleted);

        creatorTransaction.Commit();

        var ex = await Assert.ThrowsAsync<SqlException>(() => raw.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(DesignFrozen, ex.Number);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE Name = N'raw-racer';"));
    }

    [Fact]
    public async Task TheFirstExecution_SeesCommittedConfiguration_WhenADesignWriteIsInFlight()
    {
        var d = await NewDesignAsync();

        // A design write is in flight: it holds the version lock and has changed the main phase's weights so they no longer total 100.
        using var writer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var writerTransaction = writer.BeginTransaction();
        await AcquireAsync(writer, writerTransaction, d.Version);
        await writer.ExecuteAsync("DELETE dbo.ScoreRule WHERE ScoreRuleID = @r;", new { r = d.Rule2 }, writerTransaction);

        var execution = Task.Run(() => NewExecutionAsync(d.Iteration));
        await Task.Delay(500);
        Assert.False(execution.IsCompleted, "the first Execution must wait for the in-flight design write");

        writerTransaction.Commit();

        // The Execution now sees the committed configuration (only a 50.00 rule remains) and is refused.
        var ex = await Assert.ThrowsAsync<CatalogException>(() => execution.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(ErrorCatalog.InvalidRuleConfiguration.Code, ex.Entry.Code);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Execution WHERE IterationID = @i;", new { i = d.Iteration }));

        // The design is therefore still editable, and fixing it lets the first Execution through.
        await NewRuleAsync(d.Version, fixture.PhaseId, 50m);
        await NewExecutionAsync(d.Iteration);
        await FrozenAsync(() => Design().DeleteParameterAsync(d.Parameter), "after the first Execution");
    }

    [Fact]
    public async Task ManyDesignWritesRacingTheFirstExecution_NeverCommitAfterIt()
    {
        for (var round = 0; round < 5; round++)
        {
            var d = await NewDesignAsync();

            var writers = Enumerable.Range(0, 6)
                .Select(n => Task.Run(async () =>
                {
                    try
                    {
                        await Design().CreateParameterAsync(new CreateParameterRequest { ExperimentVersionID = d.Version, Name = $"race-{n}" });
                        return (Committed: true, Error: (SqlException?)null);
                    }
                    catch (SqlException ex)
                    {
                        return (Committed: false, Error: ex);
                    }
                }))
                .ToList();
            var creator = Task.Run(() => NewExecutionAsync(d.Iteration));

            var results = await Task.WhenAll(writers);
            await creator;

            // Every write is either committed (before the Execution) or rejected as frozen/retryable, never anything else...
            Assert.All(results.Where(r => !r.Committed), r => Assert.Contains(r.Error!.Number, new[] { DesignFrozen, ErrorCatalog.DeadlockVictim, ErrorCatalog.LockTimeout }));

            // ...and the committed parameters all carry a row-version order before the Execution's (proved by the
            // freeze itself: once the Execution exists, the same write is rejected).
            var committed = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE ExperimentVersionID = @v AND Name LIKE N'race-%';", new { v = d.Version });
            Assert.Equal(results.Count(r => r.Committed), committed);
            await FrozenAsync(() => Design().CreateParameterAsync(new CreateParameterRequest { ExperimentVersionID = d.Version, Name = "after" }), $"round {round}");
        }
    }
}
