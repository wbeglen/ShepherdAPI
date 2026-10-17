using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Services;
using ShepherdAPI.Api.Tests.Infrastructure;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Issue #6: the post-lock write guards, identity/ownership immutability and the parent-version lock
/// protocol, exercised with raw SQL against a real database so the triggers themselves are what is tested.
/// </summary>
[Collection("Database")]
public class LockGuardIntegrationTests(DatabaseFixture fixture)
{
    private const int VersionLocked = 50100;
    private const int IdentityChange = 50101;
    private static int _phaseSequence = 5000;

    private sealed record Ctx(
        long VersionA, long VersionB, long VersionC,
        long IterationA, long IterationA2, long IterationB,
        long ParameterA, long ParameterA2, long ParameterB,
        int RuleA1, int RuleA2, int RuleB,
        long ExecutionA, long ExecutionA2, long ExecutionA3, long ExecutionB,
        long ObservationA, long ObservationA2, long ObservationB,
        long ArtifactA, long ArtifactA2, long ArtifactB,
        int Agent2, int Phase2, int Rater2, int Category3, int Category4, int ArtifactType2);

    // ---------------------------------------------------------------- setup helpers

    private async Task<Ctx> BuildAsync()
    {
        var designRepository = new ExperimentDesignRepository(fixture.ConnectionFactory);
        var design = new ExperimentDesignService(designRepository);
        var recording = new RecordingService(new RecordingRepository(fixture.ConnectionFactory), designRepository, new LockGuardQueries(fixture.ConnectionFactory));

        async Task<long> NewVersion(string tag) => (await design.CreateExperimentVersionAsync(new CreateExperimentVersionRequest
        {
            Name = $"{tag}-{Guid.NewGuid():N}", VersionLabel = "v1", StatusID = fixture.DraftStatusId,
            EffectiveDateUTC = DateTime.UtcNow, Description = "lock guard test",
        })).ExperimentVersionID;

        async Task<long> NewIteration(long version, int sequence) => (await design.CreateIterationAsync(new CreateIterationRequest
        {
            ExperimentVersionID = version, Sequence = sequence, IterationLabel = $"i{sequence}", Prompt = "p",
        })).IterationID;

        async Task<long> NewParameter(long version, string name) => (await design.CreateParameterAsync(new CreateParameterRequest
        {
            ExperimentVersionID = version, Name = name, Value = "v",
        })).ParameterID;

        async Task<int> NewRule(long version, int category, decimal weight) => (await design.CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = version, PhaseID = fixture.PhaseId, ScoreCategoryID = category,
            WeightPercent = weight, MinScore = 0, MaxScore = 5, Active = true,
        })).ScoreRuleID;

        async Task<long> NewExecution(long iteration, int attempt) => (await recording.CreateExecutionAsync(new CreateExecutionRequest
        {
            IterationID = iteration, AgentID = fixture.AgentId, PhaseID = fixture.PhaseId, StatusID = fixture.DraftStatusId,
            StartUTC = DateTime.UtcNow, AttemptNumber = attempt,
        })).ExecutionID;

        async Task<long> NewObservation(long execution, int rule) => (await recording.CreateScoreObservationAsync(new CreateScoreObservationRequest
        {
            ExecutionID = execution, ScoreRuleID = rule, RaterID = fixture.RaterId, ObservedScore = 3m,
        })).ScoreObservationID;

        async Task<long> NewArtifact(long execution) => (await recording.CreateArtifactAsync(new CreateArtifactRequest
        {
            ExecutionID = execution, ArtifactTypeID = fixture.ArtifactTypeId, OriginalName = "e.png", OriginalURI = "file://e", StoredURI = "file://e",
            MimeType = "image/png", ByteLength = 10, SHA256 = new string('A', 64), ReceivedUTC = DateTime.UtcNow,
        })).ArtifactID;

        var a = await NewVersion("GuardA");
        var b = await NewVersion("GuardB");
        var c = await NewVersion("GuardC");

        // Each version's rules total 100.00 so Executions can be created (Issue #1).
        var ruleA1 = await NewRule(a, fixture.ScoreCategoryId, 50m);
        var ruleA2 = await NewRule(a, fixture.ScoreCategoryId2, 50m);
        var ruleB = await NewRule(b, fixture.ScoreCategoryId, 100m);
        var iterationA = await NewIteration(a, 1);
        var iterationA2 = await NewIteration(a, 2);
        var iterationB = await NewIteration(b, 1);
        var executionA = await NewExecution(iterationA, 1);
        var executionA2 = await NewExecution(iterationA, 2);
        var executionA3 = await NewExecution(iterationA, 3);
        var executionB = await NewExecution(iterationB, 1);
        var observationA = await NewObservation(executionA, ruleA1);
        var observationA2 = await NewObservation(executionA2, ruleA1);
        var observationB = await NewObservation(executionB, ruleB);
        var artifactA = await NewArtifact(executionA);
        var artifactA2 = await NewArtifact(executionA);
        var artifactB = await NewArtifact(executionB);
        await recording.AddEvidenceAsync(observationA, new CreateScoreObservationEvidenceRequest { ArtifactID = artifactA });
        await recording.AddEvidenceAsync(observationB, new CreateScoreObservationEvidenceRequest { ArtifactID = artifactB });

        return new Ctx(
            a, b, c,
            iterationA, iterationA2, iterationB,
            await NewParameter(a, "p1"), await NewParameter(a, "p2"), await NewParameter(b, "p1"),
            ruleA1, ruleA2, ruleB,
            executionA, executionA2, executionA3, executionB,
            observationA, observationA2, observationB,
            artifactA, artifactA2, artifactB,
            Agent2: await ScalarAsync<int>("INSERT dbo.Agent (Name, Provider, DefaultModel, DefaultInterface, Active) OUTPUT INSERTED.AgentID VALUES (@n, 'p', 'm', 'i', 1);", new { n = "Agent" + Guid.NewGuid().ToString("N") }),
            Phase2: await ScalarAsync<int>("INSERT dbo.Phase (Name, Sequence) OUTPUT INSERTED.PhaseID VALUES (@n, @s);", new { n = "Phase" + Guid.NewGuid().ToString("N"), s = Interlocked.Increment(ref _phaseSequence) }),
            Rater2: await ScalarAsync<int>("INSERT dbo.Rater (Name, RaterType) OUTPUT INSERTED.RaterID VALUES (@n, 'Human');", new { n = "Rater" + Guid.NewGuid().ToString("N") }),
            Category3: await ScalarAsync<int>("INSERT dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES (@n, 1);", new { n = "Cat" + Guid.NewGuid().ToString("N") }),
            Category4: await ScalarAsync<int>("INSERT dbo.ScoreCategory (Name, HigherIsBetter) OUTPUT INSERTED.ScoreCategoryID VALUES (@n, 1);", new { n = "Cat" + Guid.NewGuid().ToString("N") }),
            ArtifactType2: await ScalarAsync<int>("INSERT dbo.ArtifactType (Name) OUTPUT INSERTED.ArtifactTypeID VALUES (@n);", new { n = "Type" + Guid.NewGuid().ToString("N") }));
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

    private Task LockAsync(long version) =>
        ExecAsync($"UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = {version};");

    /// <summary>Runs the SQL, expecting it to be rejected with the given THROW number, and checks the API-level mapping.</summary>
    private async Task RejectedAsync(string sql, int number)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
        Assert.True(ex.Number == number, $"expected error {number} but got {ex.Number} ({ex.Message}) for: {sql}");

        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal(number == VersionLocked ? "version-locked" : "identity-change-rejected", body.GetProperty("code").GetString());
    }

    // ---------------------------------------------------------------- deployed objects

    [Fact]
    public async Task ProgrammabilityObjects_AreDeployed()
    {
        var triggers = (await ScalarListAsync("SELECT name FROM sys.triggers WHERE parent_class = 1;")).ToHashSet();
        foreach (var expected in new[]
        {
            "trg_ExperimentVersion_Guard", "trg_Iteration_Guard", "trg_Parameter_Guard", "trg_ScoreRule_Guard",
            "trg_Execution_Guard", "trg_ScoreObservation_Guard", "trg_Artifact_Guard", "trg_ScoreObservationEvidence_Guard",
        })
        {
            Assert.Contains(expected, triggers);
        }

        var procs = (await ScalarListAsync("SELECT name FROM sys.procedures;")).ToHashSet();
        Assert.Contains("usp_LockVersions", procs);
        Assert.Contains("usp_AssertVersionsUnlocked", procs);
        Assert.Contains("usp_AcquireVersionLock", procs);
        Assert.NotNull(await ScalarAsync<int?>("SELECT TYPE_ID(N'dbo.VersionIdList');"));
    }

    private async Task<List<string>> ScalarListAsync(string sql)
    {
        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        return (await connection.QueryAsync<string>(sql)).ToList();
    }

    [Fact]
    public async Task OwnerViews_MapEveryGovernedTableToItsVersion()
    {
        var c = await BuildAsync();
        await ExecAsync($"""
            INSERT dbo.ScoreObservationSnapshot (ExperimentVersionID, ExecutionID, PhaseID, ScoreCategoryID, ScoreRuleID, ScoreObservationID,
                                                 ObservedScore, MinScore, MaxScore, WeightPercent, HigherIsBetter, SnapshotUTC)
            VALUES ({c.VersionB}, {c.ExecutionB}, {fixture.PhaseId}, {fixture.ScoreCategoryId}, {c.RuleB}, {c.ObservationB}, 3, 0, 5, 50, 1, SYSUTCDATETIME());
            """);

        Assert.Equal(c.VersionA, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.vOwnerIteration WHERE IterationID = {c.IterationA};"));
        Assert.Equal(c.VersionA, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.vOwnerParameter WHERE ParameterID = {c.ParameterA};"));
        Assert.Equal(c.VersionA, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.vOwnerScoreRule WHERE ScoreRuleID = {c.RuleA1};"));
        Assert.Equal(c.VersionB, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.vOwnerExecution WHERE ExecutionID = {c.ExecutionB};"));
        Assert.Equal(c.VersionB, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.vOwnerArtifact WHERE ArtifactID = {c.ArtifactB};"));
        Assert.Equal(c.VersionB, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.vOwnerScoreObservation WHERE ScoreObservationID = {c.ObservationB};"));
        Assert.Equal(c.VersionB, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.vOwnerScoreObservationSnapshot WHERE ScoreObservationID = {c.ObservationB};"));

        var evidence = await ScalarAsync<(long Observation, long Artifact)>(
            $"SELECT CAST(ObservationVersionID AS bigint), CAST(ArtifactVersionID AS bigint) FROM dbo.vOwnerScoreObservationEvidence WHERE ScoreObservationID = {c.ObservationA} AND ArtifactID = {c.ArtifactA};");
        Assert.Equal((c.VersionA, c.VersionA), evidence);
    }

    // ---------------------------------------------------------------- post-lock guards

    [Fact]
    public async Task LockedVersion_RejectsInsertUpdateAndDelete_OnEveryGovernedTable()
    {
        var c = await BuildAsync();
        await LockAsync(c.VersionA);
        await LockAsync(c.VersionC);

        var statements = new (string Label, string Sql)[]
        {
            ("Iteration insert", $"INSERT dbo.Iteration (ExperimentVersionID, Sequence, IterationLabel, Prompt) VALUES ({c.VersionA}, 90, N'x', N'x');"),
            ("Iteration update", $"UPDATE dbo.Iteration SET Notes = N'x' WHERE IterationID = {c.IterationA2};"),
            ("Iteration delete", $"DELETE dbo.Iteration WHERE IterationID = {c.IterationA2};"),
            ("Parameter insert", $"INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({c.VersionA}, N'new');"),
            ("Parameter update", $"UPDATE dbo.Parameter SET Value = N'x' WHERE ParameterID = {c.ParameterA};"),
            ("Parameter delete", $"DELETE dbo.Parameter WHERE ParameterID = {c.ParameterA2};"),
            ("ScoreRule insert", $"INSERT dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active) VALUES ({c.VersionA}, {fixture.PhaseId}, {c.Category3}, 10, 0, 5, 1);"),
            ("ScoreRule update", $"UPDATE dbo.ScoreRule SET WeightPercent = 1 WHERE ScoreRuleID = {c.RuleA1};"),
            ("ScoreRule delete", $"DELETE dbo.ScoreRule WHERE ScoreRuleID = {c.RuleA2};"),
            ("Execution insert", $"INSERT dbo.Execution (IterationID, AgentID, PhaseID, StatusID, StartUTC, InterventionCount, AttemptNumber) VALUES ({c.IterationA}, {fixture.AgentId}, {fixture.PhaseId}, {fixture.DraftStatusId}, SYSUTCDATETIME(), 0, 9);"),
            ("Execution update", $"UPDATE dbo.Execution SET OutcomeNotes = N'x' WHERE ExecutionID = {c.ExecutionA};"),
            ("Execution delete", $"DELETE dbo.Execution WHERE ExecutionID = {c.ExecutionA3};"),
            ("ScoreObservation insert", $"INSERT dbo.ScoreObservation (ExecutionID, ScoreRuleID, RaterID, ObservedScore) VALUES ({c.ExecutionA3}, {c.RuleA1}, {fixture.RaterId}, 1);"),
            ("ScoreObservation update", $"UPDATE dbo.ScoreObservation SET ObservationNotes = N'x' WHERE ScoreObservationID = {c.ObservationA};"),
            ("ScoreObservation delete", $"DELETE dbo.ScoreObservation WHERE ScoreObservationID = {c.ObservationA2};"),
            ("Artifact insert", $"INSERT dbo.Artifact (ExecutionID, ArtifactTypeID, OriginalName, OriginalURI, StoredURI, MimeType, ByteLength, SHA256, ReceivedUTC) VALUES ({c.ExecutionA}, {fixture.ArtifactTypeId}, N'n', N'u', N'u', N'm', 1, REPLICATE('A', 64), SYSUTCDATETIME());"),
            ("Artifact update (no-op)", $"UPDATE dbo.Artifact SET OriginalName = OriginalName WHERE ArtifactID = {c.ArtifactA};"),
            ("Artifact delete", $"DELETE dbo.Artifact WHERE ArtifactID = {c.ArtifactA2};"),
            ("Evidence insert", $"INSERT dbo.ScoreObservationEvidence (ScoreObservationID, ArtifactID) VALUES ({c.ObservationA2}, {c.ArtifactA2});"),
            ("Evidence update", $"UPDATE dbo.ScoreObservationEvidence SET EvidenceNotes = N'x' WHERE ScoreObservationID = {c.ObservationA} AND ArtifactID = {c.ArtifactA};"),
            ("Evidence delete", $"DELETE dbo.ScoreObservationEvidence WHERE ScoreObservationID = {c.ObservationA} AND ArtifactID = {c.ArtifactA};"),
            ("ExperimentVersion Name", $"UPDATE dbo.ExperimentVersion SET Name = N'renamed' WHERE ExperimentVersionID = {c.VersionA};"),
            ("ExperimentVersion VersionLabel", $"UPDATE dbo.ExperimentVersion SET VersionLabel = N'v9' WHERE ExperimentVersionID = {c.VersionA};"),
            ("ExperimentVersion EffectiveDateUTC", $"UPDATE dbo.ExperimentVersion SET EffectiveDateUTC = DATEADD(DAY, 1, EffectiveDateUTC) WHERE ExperimentVersionID = {c.VersionA};"),
            ("ExperimentVersion Description", $"UPDATE dbo.ExperimentVersion SET Description = N'changed' WHERE ExperimentVersionID = {c.VersionA};"),
            ("ExperimentVersion clear LockedUTC", $"UPDATE dbo.ExperimentVersion SET LockedUTC = NULL WHERE ExperimentVersionID = {c.VersionA};"),
            ("ExperimentVersion change LockedUTC", $"UPDATE dbo.ExperimentVersion SET LockedUTC = DATEADD(DAY, 1, LockedUTC) WHERE ExperimentVersionID = {c.VersionA};"),
            ("ExperimentVersion delete", $"DELETE dbo.ExperimentVersion WHERE ExperimentVersionID = {c.VersionC};"),
        };

        foreach (var (label, sql) in statements)
        {
            try
            {
                await RejectedAsync(sql, VersionLocked);
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                throw new Xunit.Sdk.XunitException($"{label}: {ex.Message}");
            }
        }

        // Nothing slipped through.
        Assert.Equal(2, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Iteration WHERE ExperimentVersionID = {c.VersionA};"));
        Assert.Equal(2, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Parameter WHERE ExperimentVersionID = {c.VersionA};"));
        Assert.Equal(50m, await ScalarAsync<decimal>($"SELECT WeightPercent FROM dbo.ScoreRule WHERE ScoreRuleID = {c.RuleA1};"));
        Assert.Equal(1, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.ExperimentVersion WHERE ExperimentVersionID = {c.VersionC};"));
    }

    [Fact]
    public async Task LockedVersion_StillAllowsStatusIdChanges_AndUnlockedVersionsAreUnaffected()
    {
        var c = await BuildAsync();
        await LockAsync(c.VersionA);

        await ExecAsync($"UPDATE dbo.ExperimentVersion SET StatusID = {fixture.RetiredStatusId} WHERE ExperimentVersionID = {c.VersionA};");
        Assert.Equal(fixture.RetiredStatusId, await ScalarAsync<int>($"SELECT StatusID FROM dbo.ExperimentVersion WHERE ExperimentVersionID = {c.VersionA};"));

        // Version B is untouched by A's lock: every kind of write still works.
        await ExecAsync($"""
            UPDATE dbo.ExperimentVersion SET Name = N'renamed-B', Description = N'edited' WHERE ExperimentVersionID = {c.VersionB};
            INSERT dbo.Iteration (ExperimentVersionID, Sequence, IterationLabel, Prompt) VALUES ({c.VersionB}, 2, N'x', N'x');
            UPDATE dbo.Iteration SET Notes = N'n' WHERE ExperimentVersionID = {c.VersionB} AND Sequence = 2;
            DELETE dbo.Iteration WHERE ExperimentVersionID = {c.VersionB} AND Sequence = 2;
            UPDATE dbo.Parameter SET Value = N'new' WHERE ParameterID = {c.ParameterB};
            UPDATE dbo.ScoreRule SET WeightPercent = 60 WHERE ScoreRuleID = {c.RuleB};
            UPDATE dbo.Execution SET OutcomeNotes = N'o', EndUTC = SYSUTCDATETIME() WHERE ExecutionID = {c.ExecutionB};
            UPDATE dbo.ScoreObservation SET ObservedScore = 4, ObservationNotes = N'n' WHERE ScoreObservationID = {c.ObservationB};
            UPDATE dbo.ScoreObservationEvidence SET EvidenceNotes = N'n' WHERE ScoreObservationID = {c.ObservationB} AND ArtifactID = {c.ArtifactB};
            """);
        Assert.Equal("renamed-B", await ScalarAsync<string>($"SELECT Name FROM dbo.ExperimentVersion WHERE ExperimentVersionID = {c.VersionB};"));
    }

    [Fact]
    public async Task LockedUTC_CanBeSetFromNull_ThenNeverChanged()
    {
        var c = await BuildAsync();

        await LockAsync(c.VersionC);

        await RejectedAsync($"UPDATE dbo.ExperimentVersion SET LockedUTC = NULL WHERE ExperimentVersionID = {c.VersionC};", VersionLocked);
        await RejectedAsync($"UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = {c.VersionC} AND 1 = 1 AND LockedUTC IS NOT NULL;", VersionLocked);
    }

    // ---------------------------------------------------------------- multirow statements

    [Fact]
    public async Task MultirowStatements_MixingLockedAndUnlockedVersions_AreRejectedWhole()
    {
        var c = await BuildAsync();
        // OUTPUT without INTO is not allowed on a trigger-guarded table; SCOPE_IDENTITY() is.
        var extraB = await ScalarAsync<long>(
            $"INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({c.VersionB}, N'extraB'); SELECT CAST(SCOPE_IDENTITY() AS bigint);");
        await LockAsync(c.VersionA);

        await RejectedAsync($"INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({c.VersionB}, N'mixB'), ({c.VersionA}, N'mixA');", VersionLocked);
        Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Parameter WHERE Name = N'mixB' AND ExperimentVersionID = {c.VersionB};"));

        await RejectedAsync($"UPDATE dbo.Parameter SET Notes = N'mixed' WHERE ExperimentVersionID IN ({c.VersionA}, {c.VersionB});", VersionLocked);
        Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Parameter WHERE Notes = N'mixed';"));

        await RejectedAsync($"DELETE dbo.Parameter WHERE ParameterID IN ({c.ParameterA2}, {extraB});", VersionLocked);
        Assert.Equal(1, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Parameter WHERE ParameterID = {extraB};"));

        // The same multirow shapes are fine when every owner is unlocked.
        await ExecAsync($"UPDATE dbo.Parameter SET Notes = N'both' WHERE ParameterID IN ({c.ParameterB}, {extraB});");
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE Notes = N'both';"));
    }

    [Fact]
    public async Task RejectedStatement_InsideAnExplicitTransaction_RollsEverythingBack_AndTheConnectionStaysUsable()
    {
        var c = await BuildAsync();
        await LockAsync(c.VersionA);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync($"""
            BEGIN TRANSACTION;
            INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({c.VersionB}, N'rolled-back');
            INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({c.VersionA}, N'rejected');
            COMMIT;
            """));

        Assert.Equal(VersionLocked, ex.Number);
        Assert.Equal(0, await connection.QuerySingleAsync<int>("SELECT @@TRANCOUNT;"));
        Assert.Equal(0, await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM dbo.Parameter WHERE Name = N'rolled-back' AND ExperimentVersionID = {c.VersionB};"));
    }

    // ---------------------------------------------------------------- identity and ownership

    [Fact]
    public async Task IdentityAndOwnershipColumns_AreImmutable_OnUnlockedVersions()
    {
        var c = await BuildAsync();

        var statements = new (string Label, string Sql)[]
        {
            ("Iteration.ExperimentVersionID", $"UPDATE dbo.Iteration SET ExperimentVersionID = {c.VersionB} WHERE IterationID = {c.IterationA2};"),
            ("Parameter.ExperimentVersionID", $"UPDATE dbo.Parameter SET ExperimentVersionID = {c.VersionB} WHERE ParameterID = {c.ParameterA2};"),
            ("ScoreRule.ExperimentVersionID", $"UPDATE dbo.ScoreRule SET ExperimentVersionID = {c.VersionB} WHERE ScoreRuleID = {c.RuleA2};"),
            ("ScoreRule.PhaseID", $"UPDATE dbo.ScoreRule SET PhaseID = {c.Phase2} WHERE ScoreRuleID = {c.RuleA2};"),
            ("ScoreRule.ScoreCategoryID", $"UPDATE dbo.ScoreRule SET ScoreCategoryID = {c.Category3} WHERE ScoreRuleID = {c.RuleA2};"),
            ("Execution.IterationID", $"UPDATE dbo.Execution SET IterationID = {c.IterationB} WHERE ExecutionID = {c.ExecutionA3};"),
            ("Execution.AgentID", $"UPDATE dbo.Execution SET AgentID = {c.Agent2} WHERE ExecutionID = {c.ExecutionA3};"),
            ("Execution.PhaseID", $"UPDATE dbo.Execution SET PhaseID = {c.Phase2} WHERE ExecutionID = {c.ExecutionA3};"),
            ("Execution.AttemptNumber", $"UPDATE dbo.Execution SET AttemptNumber = 77 WHERE ExecutionID = {c.ExecutionA3};"),
            ("Execution.StartUTC", $"UPDATE dbo.Execution SET StartUTC = DATEADD(DAY, 1, StartUTC) WHERE ExecutionID = {c.ExecutionA3};"),
            ("ScoreObservation.ExecutionID", $"UPDATE dbo.ScoreObservation SET ExecutionID = {c.ExecutionB} WHERE ScoreObservationID = {c.ObservationA2};"),
            ("ScoreObservation.ScoreRuleID", $"UPDATE dbo.ScoreObservation SET ScoreRuleID = {c.RuleA2} WHERE ScoreObservationID = {c.ObservationA2};"),
            ("ScoreObservation.RaterID", $"UPDATE dbo.ScoreObservation SET RaterID = {c.Rater2} WHERE ScoreObservationID = {c.ObservationA2};"),
            ("Evidence.ScoreObservationID", $"UPDATE dbo.ScoreObservationEvidence SET ScoreObservationID = {c.ObservationB} WHERE ScoreObservationID = {c.ObservationA} AND ArtifactID = {c.ArtifactA};"),
            ("Evidence.ArtifactID", $"UPDATE dbo.ScoreObservationEvidence SET ArtifactID = {c.ArtifactB} WHERE ScoreObservationID = {c.ObservationA} AND ArtifactID = {c.ArtifactA};"),
            ("Artifact.ExecutionID", $"UPDATE dbo.Artifact SET ExecutionID = {c.ExecutionB} WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.ArtifactTypeID", $"UPDATE dbo.Artifact SET ArtifactTypeID = {c.ArtifactType2} WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.OriginalName", $"UPDATE dbo.Artifact SET OriginalName = N'other' WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.OriginalURI", $"UPDATE dbo.Artifact SET OriginalURI = N'other' WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.StoredURI", $"UPDATE dbo.Artifact SET StoredURI = N'other' WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.MimeType", $"UPDATE dbo.Artifact SET MimeType = N'text/plain' WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.ByteLength", $"UPDATE dbo.Artifact SET ByteLength = ByteLength + 1 WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.SHA256", $"UPDATE dbo.Artifact SET SHA256 = REPLICATE('B', 64) WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.ReceivedUTC", $"UPDATE dbo.Artifact SET ReceivedUTC = DATEADD(DAY, 1, ReceivedUTC) WHERE ArtifactID = {c.ArtifactA2};"),
            ("Artifact.StoredUTC", $"UPDATE dbo.Artifact SET StoredUTC = SYSUTCDATETIME() WHERE ArtifactID = {c.ArtifactA2};"),
        };

        foreach (var (label, sql) in statements)
        {
            try
            {
                await RejectedAsync(sql, IdentityChange);
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                throw new Xunit.Sdk.XunitException($"{label}: {ex.Message}");
            }
        }

        // Nothing moved.
        Assert.Equal(c.VersionA, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.Iteration WHERE IterationID = {c.IterationA2};"));
        Assert.Equal(c.VersionA, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.Parameter WHERE ParameterID = {c.ParameterA2};"));
        Assert.Equal(c.VersionA, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.ScoreRule WHERE ScoreRuleID = {c.RuleA2};"));
    }

    [Fact]
    public async Task MovingARow_OutOfALockedVersion_IsRejectedAsAnIdentityChange_AndTheRowStays()
    {
        var c = await BuildAsync();
        await LockAsync(c.VersionA);

        await RejectedAsync($"UPDATE dbo.Parameter SET ExperimentVersionID = {c.VersionB} WHERE ParameterID = {c.ParameterA2};", IdentityChange);
        await RejectedAsync($"UPDATE dbo.Iteration SET ExperimentVersionID = {c.VersionB} WHERE IterationID = {c.IterationA2};", IdentityChange);

        Assert.Equal(c.VersionA, await ScalarAsync<long>($"SELECT ExperimentVersionID FROM dbo.Parameter WHERE ParameterID = {c.ParameterA2};"));
    }

    [Fact]
    public async Task NonIdentityColumns_StillUpdateFreelyOnUnlockedVersions()
    {
        var c = await BuildAsync();

        await ExecAsync($"""
            UPDATE dbo.Iteration SET Notes = N'n', Prompt = N'p2' WHERE IterationID = {c.IterationA};
            UPDATE dbo.Parameter SET Value = N'v2', Unit = N'u' WHERE ParameterID = {c.ParameterA};
            UPDATE dbo.ScoreRule SET WeightPercent = 40, RuleNotes = N'n' WHERE ScoreRuleID = {c.RuleA1};
            UPDATE dbo.Execution SET OutcomeNotes = N'o', EndUTC = SYSUTCDATETIME(), ModelUsed = N'm' WHERE ExecutionID = {c.ExecutionA};
            UPDATE dbo.ScoreObservation SET ObservedScore = 5, ObservationNotes = N'n' WHERE ScoreObservationID = {c.ObservationA};
            UPDATE dbo.ScoreObservationEvidence SET EvidenceNotes = N'n' WHERE ScoreObservationID = {c.ObservationA} AND ArtifactID = {c.ArtifactA};
            UPDATE dbo.ExperimentVersion SET Description = N'edited' WHERE ExperimentVersionID = {c.VersionA};
            """);
    }

    // ---------------------------------------------------------------- ScoreRule soft-delete carve-out

    [Fact]
    public async Task ScoreRule_OnALockedVersion_AdmitsOnlyTheSoftDelete()
    {
        var c = await BuildAsync();
        var designRepository = new ExperimentDesignRepository(fixture.ConnectionFactory);
        var design = new ExperimentDesignService(designRepository);
        async Task<int> Rule(int category) => (await design.CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = c.VersionC, PhaseID = fixture.PhaseId, ScoreCategoryID = category,
            WeightPercent = 25m, MinScore = 0, MaxScore = 5, Active = true,
        })).ScoreRuleID;

        var r1 = await Rule(fixture.ScoreCategoryId);
        var r2 = await Rule(fixture.ScoreCategoryId2);
        var r3 = await Rule(c.Category3);
        await LockAsync(c.VersionC);

        async Task<string> State(int id) => (await ScalarAsync<string>($"SELECT RowState FROM dbo.ScoreRule WHERE ScoreRuleID = {id};")).Trim();

        // The permitted change: RowState 'A' -> 'D', nothing else.
        await ExecAsync($"UPDATE dbo.ScoreRule SET RowState = 'D' WHERE ScoreRuleID = {r1};");
        Assert.Equal("D", await State(r1));

        // Everything else stays prohibited.
        await RejectedAsync($"UPDATE dbo.ScoreRule SET RowState = 'A' WHERE ScoreRuleID = {r1};", VersionLocked);
        await RejectedAsync($"UPDATE dbo.ScoreRule SET WeightPercent = 1 WHERE ScoreRuleID = {r2};", VersionLocked);
        await RejectedAsync($"UPDATE dbo.ScoreRule SET RowState = 'X' WHERE ScoreRuleID = {r2};", VersionLocked);
        await RejectedAsync($"UPDATE dbo.ScoreRule SET RowState = 'D', RuleNotes = N'extra' WHERE ScoreRuleID = {r2};", VersionLocked);
        await RejectedAsync($"INSERT dbo.ScoreRule (ExperimentVersionID, PhaseID, ScoreCategoryID, WeightPercent, MinScore, MaxScore, Active) VALUES ({c.VersionC}, {fixture.PhaseId}, {c.Category4}, 10, 0, 5, 1);", VersionLocked);
        await RejectedAsync($"DELETE dbo.ScoreRule WHERE ScoreRuleID = {r2};", VersionLocked);
        Assert.Equal("A", await State(r2));

        // A multirow update is all-or-nothing: r1 is already 'D', so it is not an A -> D change and sinks the whole statement.
        await RejectedAsync($"UPDATE dbo.ScoreRule SET RowState = 'D' WHERE ScoreRuleID IN ({r1}, {r2});", VersionLocked);
        Assert.Equal("A", await State(r2));

        // When every row is eligible, the multirow soft-delete goes through.
        await ExecAsync($"UPDATE dbo.ScoreRule SET RowState = 'D' WHERE ScoreRuleID IN ({r2}, {r3});");
        Assert.Equal("D", await State(r2));
        Assert.Equal("D", await State(r3));
    }

    // ---------------------------------------------------------------- the lock protocol

    private static async Task<(bool Found, DateTime? LockedUtc)> AcquireAsync(IDbConnection connection, IDbTransaction? transaction, long versionId, bool exclusive)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@ExperimentVersionID", versionId);
        parameters.Add("@Exclusive", exclusive);
        parameters.Add("@Found", dbType: DbType.Boolean, direction: ParameterDirection.Output);
        parameters.Add("@LockedUTC", dbType: DbType.DateTime2, direction: ParameterDirection.Output);
        await connection.ExecuteAsync("dbo.usp_AcquireVersionLock", parameters, transaction, commandType: CommandType.StoredProcedure);
        return (parameters.Get<bool>("@Found"), parameters.Get<DateTime?>("@LockedUTC"));
    }

    private static async Task AssertRetryableAsync(SqlException ex, int expectedNumber)
    {
        Assert.Equal(expectedNumber, ex.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(ex);
        Assert.Equal(409, status);
        Assert.Equal("retryable-conflict", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AcquireVersionLock_ReportsExistenceAndLockedState_AndRequiresATransaction()
    {
        var c = await BuildAsync();
        await LockAsync(c.VersionC);

        using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using (var transaction = connection.BeginTransaction())
        {
            var unlocked = await AcquireAsync(connection, transaction, c.VersionB, exclusive: false);
            Assert.True(unlocked.Found);
            Assert.Null(unlocked.LockedUtc);

            var locked = await AcquireAsync(connection, transaction, c.VersionC, exclusive: true);
            Assert.True(locked.Found);
            Assert.NotNull(locked.LockedUtc);

            var missing = await AcquireAsync(connection, transaction, long.MaxValue, exclusive: false);
            Assert.False(missing.Found);
            transaction.Rollback();
        }

        var misuse = await Assert.ThrowsAsync<SqlException>(() => AcquireAsync(connection, null, c.VersionB, exclusive: false));
        Assert.Equal(50000, misuse.Number);
        var (status, body) = await ExceptionHandlingMiddlewareTests.RunAsync(misuse);
        Assert.Equal(500, status);
        Assert.Equal("internal-error", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task HeldUpdateLock_BlocksASecondControlledWriter_WhoFailsRetryably()
    {
        var c = await BuildAsync();

        using var holder = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var holderTransaction = holder.BeginTransaction();
        await AcquireAsync(holder, holderTransaction, c.VersionA, exclusive: false);

        using var second = await fixture.CreateConnectionFactory(300).CreateOpenConnectionAsync();
        using var secondTransaction = second.BeginTransaction();
        var ex = await Assert.ThrowsAsync<SqlException>(() => AcquireAsync(second, secondTransaction, c.VersionA, exclusive: false));

        await AssertRetryableAsync(ex, 1222);
        holderTransaction.Rollback();
    }

    [Fact]
    public async Task ExclusiveLock_WaitsForAnInFlightWriter_ThenProceeds()
    {
        var c = await BuildAsync();

        using var writer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var writerTransaction = writer.BeginTransaction();
        await AcquireAsync(writer, writerTransaction, c.VersionA, exclusive: false);

        var finalizer = Task.Run(async () =>
        {
            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            var result = await AcquireAsync(connection, transaction, c.VersionA, exclusive: true);
            transaction.Commit();
            return result;
        });

        await Task.Delay(500);
        Assert.False(finalizer.IsCompleted, "the exclusive lock must wait for the in-flight writer");

        writerTransaction.Commit();
        var acquired = await finalizer.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(acquired.Found);
    }

    [Fact]
    public async Task WriterBlockedBehindAnExclusiveLock_IsRejected_OnceFinalizationCommits()
    {
        var c = await BuildAsync();

        using var finalizer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var finalizerTransaction = finalizer.BeginTransaction();
        await AcquireAsync(finalizer, finalizerTransaction, c.VersionA, exclusive: true);

        // A plain write (no controlled procedure) reaches the guard trigger and waits on the exclusive lock.
        var lateWriter = Task.Run(async () =>
        {
            using var connection = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync($"INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({c.VersionA}, N'late');");
        });

        await Task.Delay(500);
        Assert.False(lateWriter.IsCompleted, "the writer must wait behind the exclusive lock");

        await finalizer.ExecuteAsync($"UPDATE dbo.ExperimentVersion SET LockedUTC = SYSUTCDATETIME() WHERE ExperimentVersionID = {c.VersionA};", transaction: finalizerTransaction);
        finalizerTransaction.Commit();

        var rejected = await Assert.ThrowsAsync<SqlException>(() => lateWriter.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(VersionLocked, rejected.Number);
        Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Parameter WHERE Name = N'late';"));
    }

    [Fact]
    public async Task WriterBlockedBehindAnExclusiveLock_FailsRetryably_WhenTheLockTimeoutExpires_AndLeavesNoChanges()
    {
        var c = await BuildAsync();

        using var finalizer = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
        using var finalizerTransaction = finalizer.BeginTransaction();
        await AcquireAsync(finalizer, finalizerTransaction, c.VersionA, exclusive: true);

        using var writer = await fixture.CreateConnectionFactory(300).CreateOpenConnectionAsync();
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            writer.ExecuteAsync($"INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES ({c.VersionA}, N'timed-out');"));
        finalizerTransaction.Rollback();

        await AssertRetryableAsync(ex, 1222);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE Name = N'timed-out';"));
    }

    [Fact]
    public async Task ControlledWriteProcedure_WithXactAbortOn_LeavesNoPartialChange_WhenALockWaitTimesOut()
    {
        var c = await BuildAsync();
        // A stand-in for the controlled-write procedures later issues build: lock first, write, lock another version.
        await ExecAsync("""
            CREATE OR ALTER PROCEDURE dbo.usp_TestControlledWrite @First bigint, @Second bigint AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                DECLARE @found bit, @locked datetime2(3);
                EXEC dbo.usp_AcquireVersionLock @First, 0, @found OUTPUT, @locked OUTPUT;
                INSERT dbo.Parameter (ExperimentVersionID, Name) VALUES (@First, N'partial-change');
                EXEC dbo.usp_AcquireVersionLock @Second, 0, @found OUTPUT, @locked OUTPUT;
                COMMIT;
            END
            """);
        try
        {
            using var blocker = await fixture.ConnectionFactory.CreateOpenConnectionAsync();
            using var blockerTransaction = blocker.BeginTransaction();
            await AcquireAsync(blocker, blockerTransaction, c.VersionB, exclusive: false);

            using var writer = await fixture.CreateConnectionFactory(300).CreateOpenConnectionAsync();
            var ex = await Assert.ThrowsAsync<SqlException>(() =>
                writer.ExecuteAsync("dbo.usp_TestControlledWrite", new { First = c.VersionA, Second = c.VersionB }, commandType: CommandType.StoredProcedure));
            blockerTransaction.Rollback();

            await AssertRetryableAsync(ex, 1222);
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Parameter WHERE Name = N'partial-change';"));
        }
        finally
        {
            await ExecAsync("DROP PROCEDURE IF EXISTS dbo.usp_TestControlledWrite;");
        }
    }

    [Fact]
    public async Task DeadlockVictim_BetweenTwoVersionLocks_MapsToTheRetryableConflict()
    {
        var c = await BuildAsync();

        await using var first = new SqlConnection(fixture.ConnectionString);
        await using var second = new SqlConnection(fixture.ConnectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var firstTransaction = first.BeginTransaction();
        await using var secondTransaction = second.BeginTransaction();

        await AcquireAsync(first, firstTransaction, c.VersionA, exclusive: false);
        await AcquireAsync(second, secondTransaction, c.VersionB, exclusive: false);

        async Task<SqlException?> Cross(SqlConnection connection, SqlTransaction transaction, long version)
        {
            try
            {
                await AcquireAsync(connection, transaction, version, exclusive: false);
                return null;
            }
            catch (SqlException ex)
            {
                return ex;
            }
        }

        var firstWaits = Cross(first, firstTransaction, c.VersionB);
        await Task.Delay(500);
        var secondWaits = Cross(second, secondTransaction, c.VersionA);
        var results = await Task.WhenAll(firstWaits, secondWaits);

        var victim = Assert.Single(results.Where(r => r is not null))!;
        await AssertRetryableAsync(victim, 1205);
    }
}
