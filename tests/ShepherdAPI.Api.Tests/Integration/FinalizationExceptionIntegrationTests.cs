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
/// V11: finalization with an acknowledged unknown Execution end (dbo.usp_FinalizeExperimentVersion with @UnknownEnds,
/// driven through the lock endpoint's service), the dbo.FinalizationException guard, and the corrected
/// Artifact.ByteLength CHECK constraint (upgrade/003). Real SQL Server.
/// </summary>
[Collection("Database")]
public class FinalizationExceptionIntegrationTests(DatabaseFixture fixture)
{
    private const int VersionLocked = 50100;
    private const int InsertNotPermitted = 50511;
    private const int CheckViolation = 547;
    private const string Why = "Cursor could not report the actual end time of this run.";

    private static int _sequence = 90000;

    private ExperimentDesignService Design() => new(new ExperimentDesignRepository(fixture.ConnectionFactory));

    private RecordingService Recording() =>
        new(new RecordingRepository(fixture.ConnectionFactory), new ExperimentDesignRepository(fixture.ConnectionFactory));

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

    private Task<int> NewCategoryAsync() => ScalarAsync<int>(
        "INSERT dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES (@n, 1);",
        new { n = "FECat" + Guid.NewGuid().ToString("N") });

    private Task<Execution> NewExecutionAsync(long iteration, int attempt) => Recording().CreateExecutionAsync(new CreateExecutionRequest
    {
        IterationID = iteration, AgentID = fixture.AgentId, PhaseID = fixture.PhaseId, StatusID = fixture.DraftStatusId,
        StartUTC = DateTime.UtcNow, AttemptNumber = attempt,
    });

    private Task ObserveAsync(long execution, int rule) => Recording().CreateScoreObservationAsync(new CreateScoreObservationRequest
    {
        ExecutionID = execution, ScoreRuleID = rule, RaterID = fixture.RaterId, ObservedScore = 3m,
    });

    private Task EndAsync(long execution) =>
        Recording().UpdateExecutionRunFactsAsync(execution, new UpdateExecutionRunFactsRequest { EndUTC = DateTime.UtcNow });

    /// <summary>
    /// One phase with one rule at 100, two observed Executions: E1 ended, E2 open (the Execution whose end is unknown)
    /// unless <paramref name="endBoth"/>. Optionally a third Execution E3, open and without an observation.
    /// </summary>
    private sealed record V(long Version, int Rule, long Iteration, long E1, long E2, long E3);

    private async Task<V> BuildAsync(bool endBoth = false, bool bareThird = false)
    {
        var version = (await Design().CreateExperimentVersionAsync(new CreateExperimentVersionRequest
        {
            Name = $"FE-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
            EffectiveDateUTC = DateTime.UtcNow, Description = "finalization exception test",
        })).ExperimentVersionID;
        var rule = (await Design().CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = version, PhaseID = fixture.PhaseId, ScoreCategoryID = await NewCategoryAsync(),
            WeightPercent = 100m, MinScore = 0, MaxScore = 5, Active = true,
        })).ScoreRuleID;
        var iteration = (await Design().CreateIterationAsync(new CreateIterationRequest
            { ExperimentVersionID = version, Sequence = Interlocked.Increment(ref _sequence), IterationLabel = "i", Prompt = "p" })).IterationID;

        var e1 = (await NewExecutionAsync(iteration, 1)).ExecutionID;
        var e2 = (await NewExecutionAsync(iteration, 2)).ExecutionID;
        await ObserveAsync(e1, rule);
        await ObserveAsync(e2, rule);
        await EndAsync(e1);
        if (endBoth)
        {
            await EndAsync(e2);
        }

        var e3 = bareThird ? (await NewExecutionAsync(iteration, 3)).ExecutionID : 0;
        return new V(version, rule, iteration, e1, e2, e3);
    }

    private static LockExperimentVersionRequest Unknown(params (long ExecutionID, string Explanation)[] ends) => new()
    {
        UnknownEnds = ends.Select(e => new UnknownEndAcknowledgment { ExecutionID = e.ExecutionID, Explanation = e.Explanation }).ToList(),
    };

    private Task<ExperimentVersion> FinalizeAsync(long version, LockExperimentVersionRequest? request = null) =>
        Design().LockExperimentVersionAsync(version, request ?? new LockExperimentVersionRequest());

    private Task<int> ExceptionCountAsync(long version) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FinalizationException WHERE ExperimentVersionID = @version;", new { version });

    private Task<int> SnapshotCountAsync(long version) =>
        ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = @version;", new { version });

    private async Task<DateTime?> LockedUtcAsync(long version) =>
        await ScalarAsync<DateTime?>("SELECT LockedUTC FROM dbo.ExperimentVersion WHERE ExperimentVersionID = @version;", new { version });

    private async Task<DateTime?> EndUtcAsync(long execution) =>
        await ScalarAsync<DateTime?>("SELECT EndUTC FROM dbo.Execution WHERE ExecutionID = @execution;", new { execution });

    /// <summary>Finalizes expecting the given catalog failure; nothing may be left behind. Returns the ProblemDetails body.</summary>
    private async Task<System.Text.Json.JsonElement> FailsAsync(long version, string code, LockExperimentVersionRequest? request = null)
    {
        Exception thrown;
        try
        {
            await FinalizeAsync(version, request);
            throw new Xunit.Sdk.XunitException($"finalization was expected to fail with {code}, but it succeeded");
        }
        catch (Exception ex) when (ex is CatalogException or SqlException)
        {
            thrown = ex;
        }

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(thrown);
        Assert.Equal(409, status);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Null(await LockedUtcAsync(version));
        Assert.Equal(0, await SnapshotCountAsync(version));
        Assert.Equal(0, await ExceptionCountAsync(version));
        return body;
    }

    private static long[] Ids(System.Text.Json.JsonElement body, string member) =>
        body.GetProperty(member).EnumerateArray().Select(e => e.GetInt64()).ToArray();

    /// <summary>Calls the procedure directly (bypassing the service's own validation) with the given unknown ends.</summary>
    private async Task<SqlException> ProcedureFailsAsync(long version, params (long ExecutionID, string Explanation)[] ends)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var table = new DataTable();
        table.Columns.Add("ExecutionID", typeof(long));
        table.Columns.Add("Explanation", typeof(string));
        foreach (var (id, explanation) in ends)
        {
            table.Rows.Add(id, explanation);
        }

        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", version);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
        parameters.Add("@UnknownEnds", table.AsTableValuedParameter("dbo.FinalizationExceptionList"));
        return await Assert.ThrowsAsync<SqlException>(() =>
            connection.ExecuteAsync("dbo.usp_FinalizeExperimentVersion", parameters, commandType: CommandType.StoredProcedure));
    }

    private async Task<SqlException> InsertWithTokenFailsAsync(long tokenVersion, string values)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync($"EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = {tokenVersion};");
        return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            $"INSERT dbo.FinalizationException (ExperimentVersionID, ExecutionID, ExceptionCode, Explanation, RecordedUTC) VALUES {values};"));
    }

    // ---------------------------------------------------------------- deployment

    [Fact]
    public async Task TheV11Objects_AreDeployed_AndTheScriptsAreRerunnable()
    {
        Assert.NotNull(await ScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.FinalizationException', N'U');"));
        Assert.NotNull(await ScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.trg_FinalizationException_Guard', N'TR');"));
        Assert.NotNull(await ScalarAsync<int?>("SELECT OBJECT_ID(N'dbo.vOwnerFinalizationException', N'V');"));
        Assert.NotNull(await ScalarAsync<int?>("SELECT TYPE_ID(N'dbo.FinalizationExceptionList');"));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.FinalizationException');"));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.FinalizationException') AND is_not_trusted = 0;"));

        await fixture.ApplyDeploymentScriptsAsync();
        await fixture.ApplyDeploymentScriptsAsync();

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'artifact-bytelength-greater-equal-zero' AND parent_object_id = OBJECT_ID(N'dbo.Artifact');"));
    }

    [Fact]
    public async Task TheArtifactByteLengthCheck_IsCorrected_AndTrusted()
    {
        var (definition, untrusted, disabled) = await ScalarAsync<(string, bool, bool)>(
            "SELECT definition, is_not_trusted, is_disabled FROM sys.check_constraints WHERE name = N'artifact-bytelength-greater-equal-zero';");

        Assert.Equal("([ByteLength]>=(0))", definition);
        Assert.False(untrusted);
        Assert.False(disabled);
    }

    [Fact]
    public async Task TheArtifactByteLengthCheck_RejectsANegativeLength_AndAcceptsZero()
    {
        var v = await BuildAsync();
        var sha = new string('a', 64);
        string Insert(long length) => $"""
            INSERT dbo.Artifact (ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI, MimeType, ByteLength, SHA256, ReceivedUTC)
            VALUES ({v.E1}, {fixture.ArtifactTypeId}, N'f', N'u', N's', N'text/plain', {length}, '{sha}', SYSUTCDATETIME());
            """;

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(Insert(-1)));
        Assert.Equal(CheckViolation, ex.Number);
        Assert.Contains("artifact-bytelength-greater-equal-zero", ex.Message);

        await connection.ExecuteAsync(Insert(0)); // an empty file is a valid artifact
    }

    // ---------------------------------------------------------------- success

    [Fact]
    public async Task AnAcknowledgedUnknownEnd_Finalizes_WithOneImmutableExplanation_AtTheLockTime()
    {
        var v = await BuildAsync();
        var explanation = "  " + Why + "\nLine two.  "; // stored exactly as given

        var locked = await FinalizeAsync(v.Version, Unknown((v.E2, explanation)));

        Assert.NotNull(locked.LockedUTC);
        Assert.Null(await EndUtcAsync(v.E2));
        Assert.Equal(2, await SnapshotCountAsync(v.Version)); // E2's observation is in the snapshot too
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ScoreObservationSnapshot WHERE ExperimentVersionID = @v AND ExecutionID = @e;", new { v = v.Version, e = v.E2 }));

        var row = Assert.Single(await Design().GetFinalizationExceptionsAsync(v.Version));
        Assert.Equal(v.Version, row.ExperimentVersionID);
        Assert.Equal(v.E2, row.ExecutionID);
        Assert.Equal("end-time-unknown", row.ExceptionCode);
        Assert.Equal(explanation, row.Explanation);
        Assert.Equal(locked.LockedUTC, row.RecordedUTC);
        // One database timestamp for the lock, the snapshot rows and the exception row (compared in SQL, at full precision).
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.ExperimentVersion ev
            WHERE ev.ExperimentVersionID = @v
              AND (EXISTS (SELECT 1 FROM dbo.ScoreObservationSnapshot s WHERE s.ExperimentVersionID = ev.ExperimentVersionID AND s.SnapshotUTC <> ev.LockedUTC)
                OR EXISTS (SELECT 1 FROM dbo.FinalizationException fe WHERE fe.ExperimentVersionID = ev.ExperimentVersionID AND fe.RecordedUTC <> ev.LockedUTC));
            """, new { v = v.Version }));
    }

    [Fact]
    public async Task WithoutUnknownEnds_EverythingIsAsBefore_AndNoExceptionIsRecorded()
    {
        var v = await BuildAsync(endBoth: true);

        await FinalizeAsync(v.Version);

        Assert.Equal(0, await ExceptionCountAsync(v.Version));
        Assert.Empty(await Design().GetFinalizationExceptionsAsync(v.Version));
    }

    [Fact]
    public async Task TheReadPath_IsEmptyForAnUnlockedVersion_AndNotFoundForAMissingOne()
    {
        var v = await BuildAsync();

        Assert.Empty(await Design().GetFinalizationExceptionsAsync(v.Version));
        await Assert.ThrowsAsync<NotFoundException>(() => Design().GetFinalizationExceptionsAsync(-1));
    }

    // ---------------------------------------------------------------- refused acknowledgments

    [Fact]
    public async Task AnUnlistedOpenExecution_IsStillFinalizeExecutionNotEnded_NamingOnlyIt()
    {
        var v = await BuildAsync(bareThird: true);
        await ObserveAsync(v.E3, v.Rule);

        var body = await FailsAsync(v.Version, "finalize-execution-not-ended", Unknown((v.E2, Why)));

        Assert.Equal([v.E3], Ids(body, "executionIDs"));
    }

    [Fact]
    public async Task AnAcknowledgedExecutionThatHasAnEnd_IsFinalizeUnknownEndInvalid()
    {
        var v = await BuildAsync(endBoth: true);

        var body = await FailsAsync(v.Version, "finalize-unknown-end-invalid", Unknown((v.E2, Why)));

        Assert.Equal([v.E2], Ids(body, "executionIDs"));
        Assert.Equal(1, body.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task AnAcknowledgedExecutionOfAnotherVersionOrNoneAtAll_IsFinalizeUnknownEndInvalid()
    {
        var v = await BuildAsync();
        var other = await BuildAsync();

        var body = await FailsAsync(v.Version, "finalize-unknown-end-invalid", Unknown((v.E2, Why), (other.E2, Why), (long.MaxValue, Why)));

        Assert.Equal([other.E2, long.MaxValue], Ids(body, "executionIDs"));
        Assert.Equal(0, await ExceptionCountAsync(other.Version));
    }

    [Fact]
    public async Task ABlankExplanation_IsRefusedByTheProcedure_AndByTheTable()
    {
        var v = await BuildAsync();

        // The service refuses it first (400); called directly, the procedure refuses it under the lock.
        var ex = await ProcedureFailsAsync(v.Version, (v.E2, "   "));
        Assert.Equal(50510, ex.Number);
        Assert.Null(await LockedUtcAsync(v.Version));

        // And the table itself refuses a blank explanation even from a session holding the token.
        var insert = await InsertWithTokenFailsAsync(v.Version, $"({v.Version}, {v.E2}, 'end-time-unknown', N'  ', SYSUTCDATETIME())");
        Assert.Equal(CheckViolation, insert.Number);
    }

    [Fact]
    public async Task AnAcknowledgment_DoesNotBypassScoring()
    {
        var v = await BuildAsync(bareThird: true); // E3: open and never observed

        var body = await FailsAsync(v.Version, "finalize-execution-without-observation", Unknown((v.E2, Why), (v.E3, Why)));

        Assert.Equal([v.E3], Ids(body, "executionIDs"));
    }

    [Fact]
    public async Task TheAcknowledgmentCheck_RunsBeforeTheEndCheck()
    {
        // An invalid acknowledgment AND an unlisted open Execution: 50510 comes first (4a' before 4b).
        var v = await BuildAsync(bareThird: true);
        await ObserveAsync(v.E3, v.Rule);
        await EndAsync(v.E2);

        await FailsAsync(v.Version, "finalize-unknown-end-invalid", Unknown((v.E2, Why)));
    }

    [Fact]
    public async Task PreExistingExceptionRows_RefuseFinalization()
    {
        var v = await BuildAsync();

        // Only a bypass can leave an exception row on an unlocked version: write one behind the guard's back.
        await ExecAsync("DISABLE TRIGGER dbo.trg_FinalizationException_Guard ON dbo.FinalizationException;");
        try
        {
            await ExecAsync(
                "INSERT dbo.FinalizationException (ExperimentVersionID, ExecutionID, ExceptionCode, Explanation, RecordedUTC) VALUES (@v, @e, 'end-time-unknown', N'stray', SYSUTCDATETIME());",
                new { v = v.Version, e = v.E2 });
        }
        finally
        {
            await ExecAsync("ENABLE TRIGGER dbo.trg_FinalizationException_Guard ON dbo.FinalizationException;");
        }

        var thrown = await Assert.ThrowsAsync<CatalogException>(() => FinalizeAsync(v.Version, Unknown((v.E2, Why))));
        Assert.Equal("finalize-snapshot-rows-exist", thrown.Entry.Code);
        Assert.Contains("FinalizationException", thrown.Message);
        Assert.Null(await LockedUtcAsync(v.Version));
    }

    // ---------------------------------------------------------------- rollback

    [Fact]
    public async Task AFailureAfterTheExceptionInsert_RollsBackEverything_AndClearsTheToken()
    {
        var v = await BuildAsync();
        await ExecAsync("""
            CREATE OR ALTER TRIGGER dbo.trg_TestFailLockAfterExceptions ON dbo.ExperimentVersion AFTER UPDATE AS
            BEGIN
                IF UPDATE(LockedUTC) THROW 59998, N'forced failure after the exception inserts', 1;
            END
            """);
        try
        {
            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            var table = new DataTable();
            table.Columns.Add("ExecutionID", typeof(long));
            table.Columns.Add("Explanation", typeof(string));
            table.Rows.Add(v.E2, Why);
            var parameters = new DynamicParameters();
            parameters.Add("@ExperimentVersionID", v.Version);
            parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
            parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
            parameters.Add("@UnknownEnds", table.AsTableValuedParameter("dbo.FinalizationExceptionList"));

            var ex = await Assert.ThrowsAsync<SqlException>(() =>
                connection.ExecuteAsync("dbo.usp_FinalizeExperimentVersion", parameters, commandType: CommandType.StoredProcedure));

            Assert.Equal(59998, ex.Number);
            Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
            Assert.Null(await connection.QuerySingleAsync<long?>("SELECT TRY_CONVERT(bigint, SESSION_CONTEXT(N'ShepherdAPI.FinalizingVersionID'));"));
        }
        finally
        {
            await ExecAsync("DROP TRIGGER IF EXISTS dbo.trg_TestFailLockAfterExceptions;");
        }

        Assert.Null(await LockedUtcAsync(v.Version));
        Assert.Equal(0, await SnapshotCountAsync(v.Version));
        Assert.Equal(0, await ExceptionCountAsync(v.Version));
    }

    // ---------------------------------------------------------------- the guard

    [Fact]
    public async Task InsertsWithoutTheToken_OrForAnotherVersion_OrAMismatchedExecution_AreRejected()
    {
        var v = await BuildAsync();
        var other = await BuildAsync();

        using (var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync())
        {
            var noToken = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                "INSERT dbo.FinalizationException (ExperimentVersionID, ExecutionID, ExceptionCode, Explanation, RecordedUTC) VALUES (@v, @e, 'end-time-unknown', N'x', SYSUTCDATETIME());",
                new { v = v.Version, e = v.E2 }));
            Assert.Equal(InsertNotPermitted, noToken.Number);
        }

        var otherToken = await InsertWithTokenFailsAsync(other.Version, $"({v.Version}, {v.E2}, 'end-time-unknown', N'x', SYSUTCDATETIME())");
        Assert.Equal(InsertNotPermitted, otherToken.Number);

        var mismatch = await InsertWithTokenFailsAsync(v.Version, $"({v.Version}, {other.E2}, 'end-time-unknown', N'x', SYSUTCDATETIME())");
        Assert.Equal(InsertNotPermitted, mismatch.Number);

        var mixed = await InsertWithTokenFailsAsync(v.Version,
            $"({v.Version}, {v.E2}, 'end-time-unknown', N'x', SYSUTCDATETIME()), ({other.Version}, {other.E2}, 'end-time-unknown', N'x', SYSUTCDATETIME())");
        Assert.Equal(InsertNotPermitted, mixed.Number);

        Assert.Equal(0, await ExceptionCountAsync(v.Version));
        Assert.Equal(0, await ExceptionCountAsync(other.Version));
    }

    [Fact]
    public async Task InsertsForALockedVersion_AreRejected_EvenWithTheToken()
    {
        var v = await BuildAsync(endBoth: true);
        await FinalizeAsync(v.Version);

        var ex = await InsertWithTokenFailsAsync(v.Version, $"({v.Version}, {v.E2}, 'end-time-unknown', N'late', SYSUTCDATETIME())");

        Assert.Equal(VersionLocked, ex.Number);
        Assert.Equal(0, await ExceptionCountAsync(v.Version));
    }

    [Fact]
    public async Task ExceptionRows_CannotBeUpdatedOrDeleted_EvenWithTheToken()
    {
        var v = await BuildAsync();
        await FinalizeAsync(v.Version, Unknown((v.E2, Why)));

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        await connection.ExecuteAsync($"EXEC sys.sp_set_session_context @key = N'ShepherdAPI.FinalizingVersionID', @value = {v.Version};");
        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.FinalizationException SET Explanation = N'rewritten' WHERE ExecutionID = @e;", new { e = v.E2 }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE dbo.FinalizationException WHERE ExecutionID = @e;", new { e = v.E2 }));

        Assert.Equal(VersionLocked, update.Number);
        Assert.Equal(VersionLocked, delete.Number);
        Assert.Equal(Why, Assert.Single(await Design().GetFinalizationExceptionsAsync(v.Version)).Explanation);
    }

    // ---------------------------------------------------------------- after the lock

    [Fact]
    public async Task AfterTheLock_TheUnknownEndCanNeverBeSet()
    {
        var v = await BuildAsync();
        await FinalizeAsync(v.Version, Unknown((v.E2, Why)));

        var runFacts = await Assert.ThrowsAsync<SqlException>(() => EndAsync(v.E2));
        Assert.Equal(VersionLocked, runFacts.Number);

        using (var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync())
        {
            var direct = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
                "UPDATE dbo.Execution SET EndUTC = SYSUTCDATETIME() WHERE ExecutionID = @e;", new { e = v.E2 }));
            Assert.Equal(VersionLocked, direct.Number);
        }

        var again = await Assert.ThrowsAsync<SqlException>(() => FinalizeAsync(v.Version, Unknown((v.E2, Why))));
        Assert.Equal(VersionLocked, again.Number);
        Assert.Null(await EndUtcAsync(v.E2));
        Assert.Equal(1, await ExceptionCountAsync(v.Version));
    }

    // ---------------------------------------------------------------- races

    [Fact]
    public async Task EndingTheExecution_RacingFinalization_NeverLeavesAnExceptionForAnEndedExecution()
    {
        for (var round = 0; round < 4; round++)
        {
            var v = await BuildAsync();

            var ending = Task.Run(async () =>
            {
                try
                {
                    await EndAsync(v.E2);
                    return true;
                }
                catch (SqlException ex) when (ex.Number == VersionLocked)
                {
                    return false;
                }
            });
            var finalization = Task.Run(async () =>
            {
                try
                {
                    await FinalizeAsync(v.Version, Unknown((v.E2, Why)));
                    return true;
                }
                catch (CatalogException ex) when (ex.Entry.Code == "finalize-unknown-end-invalid")
                {
                    return false;
                }
            });

            var ended = await ending;
            var finalized = await finalization;

            // Exactly one wins, and the record is consistent either way.
            Assert.NotEqual(ended, finalized);
            if (finalized)
            {
                Assert.Null(await EndUtcAsync(v.E2));
                Assert.Equal(1, await ExceptionCountAsync(v.Version));
            }
            else
            {
                Assert.NotNull(await EndUtcAsync(v.E2));
                Assert.Equal(0, await ExceptionCountAsync(v.Version));
                Assert.Null(await LockedUtcAsync(v.Version));
            }
        }
    }

    [Fact]
    public async Task ANewExecution_RacingFinalization_IsNeverLockedInUnacknowledged()
    {
        for (var round = 0; round < 4; round++)
        {
            var v = await BuildAsync();

            var creating = Task.Run(async () =>
            {
                try
                {
                    await NewExecutionAsync(v.Iteration, 10);
                    return true;
                }
                catch (SqlException ex) when (ex.Number == VersionLocked)
                {
                    return false;
                }
            });
            var finalization = Task.Run(async () =>
            {
                try
                {
                    await FinalizeAsync(v.Version, Unknown((v.E2, Why)));
                    return true;
                }
                catch (CatalogException ex) when (ex.Entry.Code is "finalize-execution-not-ended" or "finalize-execution-without-observation")
                {
                    return false;
                }
            });

            var created = await creating;
            var finalized = await finalization;

            Assert.NotEqual(created, finalized);
            if (finalized)
            {
                // Every open Execution of the locked version is acknowledged.
                Assert.Equal(0, await ScalarAsync<int>("""
                    SELECT COUNT(*) FROM dbo.Execution e JOIN dbo.Iteration i ON i.IterationID = e.IterationID
                    WHERE i.ExperimentVersionID = @v AND e.EndUTC IS NULL
                      AND NOT EXISTS (SELECT 1 FROM dbo.FinalizationException fe WHERE fe.ExecutionID = e.ExecutionID);
                    """, new { v = v.Version }));
            }
        }
    }
}
