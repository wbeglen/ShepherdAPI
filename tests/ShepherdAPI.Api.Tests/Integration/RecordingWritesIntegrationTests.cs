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
/// Issue #8: run-facts updates, Artifact creation and evidence-link addition/removal are controlled-write procedures
/// that lock the parent version FIRST, check the lock and (for run-facts) EndUTC under it, and write in one atomic
/// transaction. A deadlock-victim error (1205) gets exactly one transparent retry of the single procedure call.
/// Real SQL Server.
/// </summary>
[Collection("Database")]
public class RecordingWritesIntegrationTests(DatabaseFixture fixture)
{
    private const int VersionLocked = 50100;
    private const int RunFactsFrozen = 50201;

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

    private sealed record Ctx(long Version, long Iteration, long Execution, long Execution2, int Rule, long Observation, long Observation2, long Artifact);

    /// <summary>A finalizable version: one rule at 100, two Executions (ended), an observation on each, one Artifact.</summary>
    private async Task<Ctx> BuildAsync(bool endExecutions = true)
    {
        var design = Design();
        var recording = Recording();
        var version = (await design.CreateExperimentVersionAsync(new CreateExperimentVersionRequest
        {
            Name = $"RecWrites-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
            EffectiveDateUTC = DateTime.UtcNow, Description = "recording writes test",
        })).ExperimentVersionID;
        var rule = (await design.CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = version, PhaseID = fixture.PhaseId, ScoreCategoryID = fixture.ScoreCategoryId,
            WeightPercent = 100m, MinScore = 0, MaxScore = 5, Active = true,
        })).ScoreRuleID;
        var iteration = (await design.CreateIterationAsync(new CreateIterationRequest
            { ExperimentVersionID = version, Sequence = 1, IterationLabel = "i1", Prompt = "p" })).IterationID;

        async Task<long> NewExecution(int attempt) => (await recording.CreateExecutionAsync(new CreateExecutionRequest
        {
            IterationID = iteration, AgentID = fixture.AgentId, PhaseID = fixture.PhaseId, StatusID = fixture.DraftStatusId,
            StartUTC = DateTime.UtcNow, AttemptNumber = attempt,
        })).ExecutionID;

        var e1 = await NewExecution(1);
        var e2 = await NewExecution(2);
        var o1 = (await recording.CreateScoreObservationAsync(new CreateScoreObservationRequest
            { ExecutionID = e1, ScoreRuleID = rule, RaterID = fixture.RaterId, ObservedScore = 3m })).ScoreObservationID;
        var rater2 = await ScalarAsync<int>("INSERT dbo.Rater (Name, RaterType) OUTPUT INSERTED.RaterID VALUES (@n, 'Human');", new { n = "RecRater" + Guid.NewGuid().ToString("N") });
        var o2 = (await recording.CreateScoreObservationAsync(new CreateScoreObservationRequest
            { ExecutionID = e2, ScoreRuleID = rule, RaterID = rater2, ObservedScore = 4m })).ScoreObservationID;
        var artifact = (await recording.CreateArtifactAsync(NewArtifactRequest(e1))).ArtifactID;

        if (endExecutions)
        {
            await recording.UpdateExecutionRunFactsAsync(e1, new UpdateExecutionRunFactsRequest { EndUTC = DateTime.UtcNow });
            await recording.UpdateExecutionRunFactsAsync(e2, new UpdateExecutionRunFactsRequest { EndUTC = DateTime.UtcNow });
        }

        return new Ctx(version, iteration, e1, e2, rule, o1, o2, artifact);
    }

    private CreateArtifactRequest NewArtifactRequest(long execution, char hash = 'A') => new()
    {
        ExecutionID = execution, ArtifactTypeID = fixture.ArtifactTypeId, OriginalName = "e.png", OriginalURI = "file://e", StoredURI = "file://e",
        MimeType = "image/png", ByteLength = 10, SHA256 = new string(hash, 64), ReceivedUTC = DateTime.UtcNow,
    };

    private static async Task AssertMappedAsync(SqlException ex, int number, string code)
    {
        Assert.Equal(number, ex.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal(code, body.GetProperty("code").GetString());
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

    private Task<int> ArtifactCountAsync(long execution) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Artifact WHERE ExecutionID = @execution;", new { execution });

    /// <summary>Counts how many connections (= procedure calls) an operation opens.</summary>
    private sealed class CountingFactory(ISqlConnectionFactory inner) : ISqlConnectionFactory
    {
        public int Opened;

        public Task<IDbConnection> CreateOpenConnectionAsync()
        {
            Interlocked.Increment(ref Opened);
            return inner.CreateOpenConnectionAsync();
        }
    }

    // ---------------------------------------------------------------- deployment

    [Fact]
    public async Task TheRecordingWriteProcedures_AreDeployed_AndNoLongerUseTheLockGuardQueries()
    {
        var procs = (await ScalarListAsync("SELECT name FROM sys.procedures;")).ToHashSet();
        foreach (var expected in new[] { "usp_UpdateExecutionRunFacts", "usp_CreateArtifact", "usp_AddEvidence", "usp_RemoveEvidence" })
        {
            Assert.Contains(expected, procs);
        }

        Assert.DoesNotContain(typeof(RecordingService).GetConstructors().Single().GetParameters(),
            p => p.ParameterType.Name == "ILockGuardQueries");
    }

    private async Task<List<string>> ScalarListAsync(string sql)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        return (await connection.QueryAsync<string>(sql)).ToList();
    }

    // ---------------------------------------------------------------- run-facts

    [Fact]
    public async Task RunFacts_AreUpdatedOnce_ThenFrozenByEndUtc_UnderTheLock()
    {
        var c = await BuildAsync(endExecutions: false);

        var updated = await Recording().UpdateExecutionRunFactsAsync(c.Execution, new UpdateExecutionRunFactsRequest
            { ModelUsed = "m", InterfaceUsed = "i", InterventionCount = 2, OutcomeNotes = "n" });
        Assert.Equal(("m", "i", 2, "n"), (updated.ModelUsed, updated.InterfaceUsed, updated.InterventionCount, updated.OutcomeNotes));
        Assert.Null(updated.EndUTC);

        var ended = await Recording().UpdateExecutionRunFactsAsync(c.Execution, new UpdateExecutionRunFactsRequest { InterventionCount = 3, EndUTC = DateTime.UtcNow });
        Assert.NotNull(ended.EndUTC);

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            Recording().UpdateExecutionRunFactsAsync(c.Execution, new UpdateExecutionRunFactsRequest { InterventionCount = 9, OutcomeNotes = "too late" }));
        await AssertMappedAsync(ex, RunFactsFrozen, "execution-run-facts-frozen");
        Assert.Equal(3, (await Recording().GetExecutionAsync(c.Execution)).InterventionCount);
    }

    [Fact]
    public async Task RunFacts_OnALockedVersion_ReportVersionLocked_BeforeExecutionRunFactsFrozen()
    {
        var c = await BuildAsync(); // both Executions are ended
        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, c.Version);

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            Recording().UpdateExecutionRunFactsAsync(c.Execution, new UpdateExecutionRunFactsRequest { OutcomeNotes = "x" }));

        await AssertMappedAsync(ex, VersionLocked, "version-locked");
    }

    [Fact]
    public async Task RunFacts_OnALockedVersion_WithAnOpenExecution_AreVersionLocked()
    {
        var c = await BuildAsync(endExecutions: false);
        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, c.Version);

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            Recording().UpdateExecutionRunFactsAsync(c.Execution, new UpdateExecutionRunFactsRequest { OutcomeNotes = "x" }));

        await AssertMappedAsync(ex, VersionLocked, "version-locked");
    }

    [Fact]
    public async Task RunFacts_OfAMissingExecution_AreNotFound_AndStartNoTransaction()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Recording().UpdateExecutionRunFactsAsync(long.MaxValue, new UpdateExecutionRunFactsRequest()));
    }

    [Fact]
    public async Task TwoRacingEndUtcUpdates_LetOnlyOneWin_TheOtherSeesTheFrozenRestrictionUnderTheLock()
    {
        var c = await BuildAsync(endExecutions: false);

        var racers = Enumerable.Range(1, 6).Select(n => Task.Run(async () =>
        {
            try
            {
                await Recording().UpdateExecutionRunFactsAsync(c.Execution, new UpdateExecutionRunFactsRequest
                    { InterventionCount = n, OutcomeNotes = $"racer {n}", EndUTC = DateTime.UtcNow });
                return (Won: true, Number: 0);
            }
            catch (SqlException ex)
            {
                return (Won: false, ex.Number);
            }
        })).ToList();
        var results = await Task.WhenAll(racers);

        Assert.Equal(1, results.Count(r => r.Won));
        Assert.All(results.Where(r => !r.Won), r => Assert.Equal(RunFactsFrozen, r.Number));

        // The winner's values are the stored ones: nobody overwrote them after EndUTC was set.
        var stored = await Recording().GetExecutionAsync(c.Execution);
        Assert.Equal($"racer {stored.InterventionCount}", stored.OutcomeNotes);
    }

    // ---------------------------------------------------------------- artifacts

    [Fact]
    public async Task ArtifactCreation_Works_AndIsRejectedOnALockedVersion()
    {
        var c = await BuildAsync();

        var created = await Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution2, 'C'));
        Assert.Equal("C", created.SHA256[..1]);
        Assert.Equal(c.Execution2, created.ExecutionID);

        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, c.Version);
        var before = await ArtifactCountAsync(c.Execution2);
        var ex = await Assert.ThrowsAsync<SqlException>(() => Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution2, 'D')));
        await AssertMappedAsync(ex, VersionLocked, "version-locked");
        Assert.Equal(before, await ArtifactCountAsync(c.Execution2));
    }

    [Fact]
    public async Task ArtifactCreation_ForAMissingExecution_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Recording().CreateArtifactAsync(NewArtifactRequest(long.MaxValue)));
    }

    [Fact]
    public async Task ArtifactCreation_StaysAllowedAfterEndUtc_WhileTheVersionIsNotFinal()
    {
        var c = await BuildAsync(); // both Executions ended

        var created = await Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution, 'E'));

        Assert.Equal(c.Execution, created.ExecutionID);
    }

    // ---------------------------------------------------------------- evidence

    [Fact]
    public async Task Evidence_IsAddedAndRemoved_WhileUnlocked()
    {
        var c = await BuildAsync();

        var link = await Recording().AddEvidenceAsync(c.Observation, new CreateScoreObservationEvidenceRequest { ArtifactID = c.Artifact, EvidenceNotes = "n" });
        Assert.Equal((c.Observation, c.Artifact), (link.ScoreObservationID, link.ArtifactID));
        Assert.Single(await Recording().GetEvidenceAsync(c.Observation));

        await Recording().RemoveEvidenceAsync(c.Observation, c.Artifact);
        Assert.Empty(await Recording().GetEvidenceAsync(c.Observation));
    }

    [Fact]
    public async Task Evidence_DuplicateAndMissingLinks_KeepTheirMappings()
    {
        var c = await BuildAsync();
        await Recording().AddEvidenceAsync(c.Observation, new CreateScoreObservationEvidenceRequest { ArtifactID = c.Artifact });

        var duplicate = await Assert.ThrowsAsync<SqlException>(() =>
            Recording().AddEvidenceAsync(c.Observation, new CreateScoreObservationEvidenceRequest { ArtifactID = c.Artifact }));
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(duplicate);
        Assert.Equal(409, status);
        Assert.Equal("duplicate-key", body.GetProperty("code").GetString());
        Assert.Single(await Recording().GetEvidenceAsync(c.Observation)); // no second row, no partial write

        await Assert.ThrowsAsync<NotFoundException>(() => Recording().RemoveEvidenceAsync(c.Observation2, c.Artifact));
        await Assert.ThrowsAsync<NotFoundException>(() => Recording().AddEvidenceAsync(long.MaxValue, new CreateScoreObservationEvidenceRequest { ArtifactID = c.Artifact }));
    }

    [Fact]
    public async Task RemovingAMissingLink_OnALockedVersion_IsNotFound_NotVersionLocked()
    {
        // Behavior change (Issue #8): the procedure looks the link up before it checks the lock, so a link that does not
        // exist is a 404 even on a locked version. Previously the service checked the lock first and answered 409.
        var c = await BuildAsync();
        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, c.Version);

        await Assert.ThrowsAsync<NotFoundException>(() => Recording().RemoveEvidenceAsync(c.Observation, c.Artifact));
        await Assert.ThrowsAsync<NotFoundException>(() => Recording().RemoveEvidenceAsync(c.Observation2, c.Artifact));
    }

    [Fact]
    public async Task Evidence_IsRejectedOnALockedVersion_ForBothAddAndRemove()
    {
        var c = await BuildAsync();
        await Recording().AddEvidenceAsync(c.Observation, new CreateScoreObservationEvidenceRequest { ArtifactID = c.Artifact });
        var second = (await Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution, 'F'))).ArtifactID;
        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, c.Version);

        var add = await Assert.ThrowsAsync<SqlException>(() =>
            Recording().AddEvidenceAsync(c.Observation, new CreateScoreObservationEvidenceRequest { ArtifactID = second }));
        await AssertMappedAsync(add, VersionLocked, "version-locked");

        var remove = await Assert.ThrowsAsync<SqlException>(() => Recording().RemoveEvidenceAsync(c.Observation, c.Artifact));
        await AssertMappedAsync(remove, VersionLocked, "version-locked");

        Assert.Single(await Recording().GetEvidenceAsync(c.Observation)); // neither changed the links
    }

    [Fact]
    public async Task AnEvidenceLinkSpanningTwoVersions_LocksBoth_AndIsRejectedIfEitherIsLocked()
    {
        // The service refuses cross-Execution links, so call the repository: the database itself does not, and the
        // procedure must still follow the lock protocol for both owners.
        var a = await BuildAsync();
        var b = await BuildAsync();
        var repository = new RecordingRepository(fixture.ConnectionFactory);

        await LockedUtcTestHelper.LockAsync(fixture.ConnectionFactory, b.Version);
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            repository.CreateEvidenceLinkAsync(new ScoreObservationEvidence { ScoreObservationID = a.Observation, ArtifactID = b.Artifact }));

        Assert.Equal(VersionLocked, ex.Number); // the artifact's version is locked even though the observation's is not
        Assert.Empty(await Recording().GetEvidenceAsync(a.Observation));
    }

    [Fact]
    public async Task AnEvidenceLinkSpanningTwoVersions_WaitsForTheSecondVersionsLock()
    {
        var a = await BuildAsync();
        var b = await BuildAsync();

        // Another writer holds the artifact's version; a link needing it must wait, proving that version is locked too.
        using var holder = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var holderTransaction = holder.BeginTransaction();
        await AcquireAsync(holder, holderTransaction, b.Version);

        var shortTimeout = new RecordingRepository(fixture.CreateConnectionFactory(300));
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            shortTimeout.CreateEvidenceLinkAsync(new ScoreObservationEvidence { ScoreObservationID = a.Observation, ArtifactID = b.Artifact }));
        holderTransaction.Rollback();

        Assert.Equal(ErrorCatalog.LockTimeout, ex.Number);
        Assert.Empty(await Recording().GetEvidenceAsync(a.Observation));
    }

    // ---------------------------------------------------------------- coordination with finalization

    [Fact]
    public async Task Writes_WaitBehindFinalizationsExclusiveLock_ThenAreRejectedAsVersionLocked()
    {
        var c = await BuildAsync(); // both Executions are ended
        var linked = (await Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution, '1'))).ArtifactID;
        await Recording().AddEvidenceAsync(c.Observation, new CreateScoreObservationEvidenceRequest { ArtifactID = linked });
        var secondExecutionArtifact = (await Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution2, '2'))).ArtifactID;
        var artifactsBefore = await ArtifactCountAsync(c.Execution2);

        using var finalizer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var finalizerTransaction = finalizer.BeginTransaction();
        await AcquireAsync(finalizer, finalizerTransaction, c.Version, exclusive: true);

        var writes = new Func<Task>[]
        {
            () => Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution2, '7')),
            () => Recording().AddEvidenceAsync(c.Observation2, new CreateScoreObservationEvidenceRequest { ArtifactID = secondExecutionArtifact }),
            () => Recording().RemoveEvidenceAsync(c.Observation, linked),
            () => Recording().UpdateExecutionRunFactsAsync(c.Execution2, new UpdateExecutionRunFactsRequest { OutcomeNotes = "late" }),
        }.Select(w => Task.Run(w)).ToList();

        await Task.Delay(700);
        Assert.All(writes, t => Assert.False(t.IsCompleted, "each write must wait behind the exclusive lock"));

        await LockedUtcTestHelper.LockAsync(finalizer, c.Version, finalizerTransaction);
        finalizerTransaction.Commit();

        foreach (var write in writes)
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => write.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(VersionLocked, ex.Number); // not execution-run-facts-frozen, although Execution2 is ended too
        }

        Assert.Equal(artifactsBefore, await ArtifactCountAsync(c.Execution2));
        Assert.Single(await Recording().GetEvidenceAsync(c.Observation)); // the existing link was not removed
        Assert.Empty(await Recording().GetEvidenceAsync(c.Observation2));
        Assert.Null((await Recording().GetExecutionAsync(c.Execution2)).OutcomeNotes);
    }

    [Fact]
    public async Task RealFinalization_RacingTheFourWrites_NeverDeadlocksAndNeverLosesAWrite()
    {
        for (var round = 0; round < 3; round++)
        {
            var c = await BuildAsync();
            var extra = (await Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution, '3'))).ArtifactID;
            await Recording().AddEvidenceAsync(c.Observation, new CreateScoreObservationEvidenceRequest { ArtifactID = extra });
            var secondExecutionArtifact = (await Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution2, '4'))).ArtifactID;

            Task<(string Kind, bool Committed, int Number)> Track(string kind, Func<Task> write) => Task.Run(async () =>
            {
                try
                {
                    await write();
                    return (kind, true, 0);
                }
                catch (SqlException ex)
                {
                    return (kind, false, ex.Number);
                }
            });

            var writes = new List<Task<(string Kind, bool Committed, int Number)>>
            {
                Track("artifact", () => Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution2, '5'))),
                Track("artifact", () => Recording().CreateArtifactAsync(NewArtifactRequest(c.Execution2, '6'))),
                Track("add", () => Recording().AddEvidenceAsync(c.Observation2, new CreateScoreObservationEvidenceRequest { ArtifactID = secondExecutionArtifact })),
                Track("remove", () => Recording().RemoveEvidenceAsync(c.Observation, extra)),
            };
            var finalization = Task.Run(async () =>
            {
                try
                {
                    await Design().LockExperimentVersionAsync(c.Version, new LockExperimentVersionRequest());
                    return (Succeeded: true, Number: 0);
                }
                catch (SqlException ex)
                {
                    return (Succeeded: false, ex.Number);
                }
            });

            var results = await Task.WhenAll(writes);
            var finalized = await finalization;

            // Parent-first locking: no deadlock victim and no lock timeout anywhere, and finalization always succeeds.
            Assert.True(finalized.Succeeded, $"finalization failed with {finalized.Number}");
            Assert.DoesNotContain(results, r => r.Number is 1205 or 1222);
            Assert.All(results.Where(r => !r.Committed), r => Assert.Equal(VersionLocked, r.Number));

            // What committed before the lock is exactly what is there now.
            Assert.NotNull(await ScalarAsync<DateTime?>("SELECT LockedUTC FROM dbo.ExperimentVersion WHERE ExperimentVersionID = @v;", new { v = c.Version }));
            Assert.Equal(1 + results.Count(r => r.Kind == "artifact" && r.Committed), await ArtifactCountAsync(c.Execution2));
            Assert.Equal(results.Single(r => r.Kind == "add").Committed ? 1 : 0, (await Recording().GetEvidenceAsync(c.Observation2)).Count);
            Assert.Equal(results.Single(r => r.Kind == "remove").Committed ? 0 : 1, (await Recording().GetEvidenceAsync(c.Observation)).Count);
        }
    }

    // ---------------------------------------------------------------- the deadlock retry, against real deadlocks

    /// <summary>
    /// Forces a real deadlock. The blocker (high deadlock priority) holds a shared lock on something the procedure writes
    /// only AFTER it has taken the parent-version lock; the procedure therefore waits holding the version lock, and the
    /// blocker then asks for that lock, which closes the cycle. The server kills the procedure's first attempt (1205);
    /// once the blocker commits, the single retry runs the whole procedure again. Returns the operation's result and
    /// the number of connections (= procedure calls) the repository opened.
    /// </summary>
    private async Task<(T Result, int ProcedureCalls)> RunWithOneDeadlockAsync<T>(long version, string blockerSql, object blockerArgs, Func<RecordingRepository, Task<T>> operation)
    {
        var counting = new CountingFactory(fixture.CreateConnectionFactory(30000));

        using var blocker = await fixture.CreateConnectionFactory(null).CreateOpenConnectionAsync();
        using var blockerTransaction = blocker.BeginTransaction();
        await blocker.ExecuteAsync("SET DEADLOCK_PRIORITY HIGH;", transaction: blockerTransaction);
        await blocker.ExecuteAsync(blockerSql, blockerArgs, blockerTransaction);

        var writer = Task.Run(() => operation(new RecordingRepository(counting)));
        await Task.Delay(1000);
        Assert.False(writer.IsCompleted, "the procedure must be waiting on the blocker's lock");

        await AcquireAsync(blocker, blockerTransaction, version); // closes the cycle; waits until the server resolves it
        blockerTransaction.Commit();

        var result = await writer.WaitAsync(TimeSpan.FromSeconds(30));
        return (result, counting.Opened);
    }

    [Fact]
    public async Task RunFactsUpdate_ChosenAsADeadlockVictim_IsRetriedOnce_AndSucceeds()
    {
        var c = await BuildAsync(endExecutions: false);

        // A shared lock on the Execution row does not stop the procedure's early reads but blocks its UPDATE.
        var (found, calls) = await RunWithOneDeadlockAsync(c.Version,
            "SELECT ExecutionID FROM dbo.Execution WITH (HOLDLOCK, ROWLOCK) WHERE ExecutionID = @id;", new { id = c.Execution },
            repository => repository.UpdateExecutionRunFactsAsync(c.Execution, null, null, 7, "retried", null));

        Assert.True(found);
        Assert.Equal(2, calls); // the deadlock victim, then exactly one retry
        var stored = await Recording().GetExecutionAsync(c.Execution);
        Assert.Equal((7, "retried"), (stored.InterventionCount, stored.OutcomeNotes));
    }

    [Fact]
    public async Task ArtifactCreation_ChosenAsADeadlockVictim_IsRetriedOnce_WithoutADuplicate()
    {
        var c = await BuildAsync();
        var before = await ArtifactCountAsync(c.Execution);

        // A shared table lock on Artifact is compatible with every early read but blocks the INSERT.
        var (id, calls) = await RunWithOneDeadlockAsync(c.Version,
            "SELECT TOP (1) ArtifactID FROM dbo.Artifact WITH (TABLOCK, HOLDLOCK);", new { },
            repository => repository.CreateArtifactAsync(new Artifact
            {
                ExecutionID = c.Execution, ArtifactTypeID = fixture.ArtifactTypeId, OriginalName = "r.png", OriginalURI = "file://r",
                StoredURI = "file://r", MimeType = "image/png", ByteLength = 1, SHA256 = new string('8', 64), ReceivedUTC = DateTime.UtcNow,
            }));

        Assert.NotNull(id);
        Assert.Equal(2, calls);
        Assert.Equal(before + 1, await ArtifactCountAsync(c.Execution)); // the failed first attempt left no row
    }

    [Fact]
    public async Task ALockTimeout_IsNotRetried_AndIsTheRetryableConflict()
    {
        var c = await BuildAsync(endExecutions: false);

        using var holder = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var holderTransaction = holder.BeginTransaction();
        await AcquireAsync(holder, holderTransaction, c.Version, exclusive: true);

        var counting = new CountingFactory(fixture.CreateConnectionFactory(300));
        var repository = new RecordingRepository(counting);
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            repository.UpdateExecutionRunFactsAsync(c.Execution, null, null, 1, "timeout", null));
        holderTransaction.Rollback();

        Assert.Equal(ErrorCatalog.LockTimeout, ex.Number);
        Assert.Equal(1, counting.Opened); // one procedure call: no transparent retry
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal("retryable-conflict", body.GetProperty("code").GetString());
        Assert.Null((await Recording().GetExecutionAsync(c.Execution)).OutcomeNotes);
    }
}
