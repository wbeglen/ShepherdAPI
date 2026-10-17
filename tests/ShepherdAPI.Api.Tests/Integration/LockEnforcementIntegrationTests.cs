using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Services;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Exercises the lock/immutability invariant from docs/ARCHITECTURE.md §5 against a real SQL
/// Server database (see DatabaseFixture) instead of mocks, so the actual SQL and constraint
/// interactions are verified, not just the C# logic around them.
/// </summary>
[Collection("Database")]
public class LockEnforcementIntegrationTests(DatabaseFixture fixture)
{
    private ExperimentDesignService CreateDesignService() =>
        new(new ExperimentDesignRepository(fixture.ConnectionFactory));

    private RecordingService CreateRecordingService(out IExperimentDesignRepository designRepo)
    {
        designRepo = new ExperimentDesignRepository(fixture.ConnectionFactory);
        var recordingRepo = new RecordingRepository(fixture.ConnectionFactory);
        var lockGuard = new LockGuardQueries(fixture.ConnectionFactory);
        return new RecordingService(recordingRepo, designRepo, lockGuard);
    }

    private async Task<(Models.ExperimentVersion Version, Models.Iteration Iteration, Models.Execution Execution, int RuleId)> CreateBaseHierarchyAsync(
        ExperimentDesignService designService, RecordingService recordingService)
    {
        var version = await designService.CreateExperimentVersionAsync(new CreateExperimentVersionRequest
        {
            Name = $"Test-{Guid.NewGuid():N}",
            VersionLabel = "v1",
            StatusID = fixture.DraftStatusId,
            EffectiveDateUTC = DateTime.UtcNow,
            Description = "Integration test experiment version.",
        });

        // An Execution needs the phase's active rules to total 100.00 (Issue #1), so configure them first.
        var ruleId = await CreateScoreRuleAsync(designService, version.ExperimentVersionID);

        var iteration = await designService.CreateIterationAsync(new CreateIterationRequest
        {
            ExperimentVersionID = version.ExperimentVersionID,
            Sequence = 1,
            IterationLabel = "Iteration 1",
            Prompt = "Do the thing.",
        });

        var execution = await recordingService.CreateExecutionAsync(new CreateExecutionRequest
        {
            IterationID = iteration.IterationID,
            AgentID = fixture.AgentId,
            PhaseID = fixture.PhaseId,
            StatusID = fixture.DraftStatusId,
            StartUTC = DateTime.UtcNow,
            InterventionCount = 0,
            AttemptNumber = 1,
        });

        return (version, iteration, execution, ruleId);
    }

    [Fact]
    public async Task FullRecordingFlow_ThenLock_BlocksFurtherMutationAndCreation()
    {
        var designService = CreateDesignService();
        var recordingService = CreateRecordingService(out _);
        var (version, iteration, execution, ruleId) = await CreateBaseHierarchyAsync(designService, recordingService);

        var observation = await recordingService.CreateScoreObservationAsync(new CreateScoreObservationRequest
        {
            ExecutionID = execution.ExecutionID,
            ScoreRuleID = ruleId,
            RaterID = fixture.RaterId,
            ObservedScore = 3.5m,
        });

        var artifact = await recordingService.CreateArtifactAsync(new CreateArtifactRequest
        {
            ExecutionID = execution.ExecutionID,
            ArtifactTypeID = fixture.ArtifactTypeId,
            OriginalName = "evidence.png",
            OriginalURI = "file://local/evidence.png",
            StoredURI = "file://local/evidence.png",
            MimeType = "image/png",
            ByteLength = 1024,
            SHA256 = new string('A', 64),
            ReceivedUTC = DateTime.UtcNow,
        });

        await recordingService.AddEvidenceAsync(observation.ScoreObservationID, new CreateScoreObservationEvidenceRequest
        {
            ArtifactID = artifact.ArtifactID,
        });

        // Corrections while unlocked succeed.
        var corrected = await recordingService.UpdateScoreObservationContentAsync(observation.ScoreObservationID,
            new UpdateScoreObservationContentRequest { ObservedScore = 4.0m, ObservationNotes = "Revised after later execution." });
        Assert.Equal(4.0m, corrected.ObservedScore);

        // Lock the experimental record.
        var locked = await designService.LockExperimentVersionAsync(version.ExperimentVersionID,
            new LockExperimentVersionRequest { StatusID = fixture.LockedStatusId });
        Assert.NotNull(locked.LockedUTC);

        // Everything governed by the lock now rejects mutation and creation.
        // Design writes are checked inside their controlled-write procedures (Issue #4), so a locked version comes
        // back as the catalog's version-locked error (SQL 50100), reported before the design-frozen check.
        var lockedUpdate0 = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => designService.UpdateExperimentVersionAsync(
            version.ExperimentVersionID, new UpdateExperimentVersionRequest
            {
                Name = version.Name, VersionLabel = version.VersionLabel, EffectiveDateUTC = version.EffectiveDateUTC, Description = "changed",
            }));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, lockedUpdate0.Number);

        var lockedIteration = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => designService.CreateIterationAsync(new CreateIterationRequest
        {
            ExperimentVersionID = version.ExperimentVersionID, Sequence = 2, IterationLabel = "Too late", Prompt = "Nope.",
        }));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, lockedIteration.Number);

        // Execution creation is checked inside dbo.usp_CreateExecution, so a locked version comes back as the
        // catalog's version-locked error (SQL 50100) rather than a service-layer RecordLockedException.
        var lockedCreate = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => recordingService.CreateExecutionAsync(new CreateExecutionRequest
        {
            IterationID = iteration.IterationID, AgentID = fixture.AgentId, PhaseID = fixture.PhaseId, StatusID = fixture.DraftStatusId,
            StartUTC = DateTime.UtcNow, InterventionCount = 0, AttemptNumber = 2,
        }));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, lockedCreate.Number);

        // Observation updates are checked inside dbo.usp_UpdateScoreObservationContent (Issue #3), so a locked
        // version comes back as the catalog's version-locked error (SQL 50100).
        var lockedUpdate = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => recordingService.UpdateScoreObservationContentAsync(
            observation.ScoreObservationID, new UpdateScoreObservationContentRequest { ObservedScore = 5.0m }));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, lockedUpdate.Number);

        await Assert.ThrowsAsync<RecordLockedException>(() => recordingService.AddEvidenceAsync(
            observation.ScoreObservationID, new CreateScoreObservationEvidenceRequest { ArtifactID = artifact.ArtifactID }));

        await Assert.ThrowsAsync<RecordLockedException>(() => designService.LockExperimentVersionAsync(
            version.ExperimentVersionID, new LockExperimentVersionRequest { StatusID = fixture.LockedStatusId }));

        // StatusID is the sole post-lock exception.
        var retired = await designService.SetExperimentVersionStatusAsync(
            version.ExperimentVersionID, new SetExperimentVersionStatusRequest { StatusID = fixture.RetiredStatusId });
        Assert.Equal(fixture.RetiredStatusId, retired.StatusID);
        Assert.NotNull(retired.LockedUTC); // Retirement does not clear the lock.
    }

    [Fact]
    public async Task ExecutionEndUtc_LocksRunFacts_IndependentlyOfExperimentVersionLock()
    {
        var designService = CreateDesignService();
        var recordingService = CreateRecordingService(out _);
        var (_, _, execution, _) = await CreateBaseHierarchyAsync(designService, recordingService);

        var completed = await recordingService.UpdateExecutionRunFactsAsync(execution.ExecutionID, new UpdateExecutionRunFactsRequest
        {
            InterventionCount = 1,
            OutcomeNotes = "Finished successfully.",
            EndUTC = DateTime.UtcNow,
        });
        Assert.NotNull(completed.EndUTC);

        // The ExperimentVersion itself is still unlocked, but the Execution's own run-facts are
        // now frozen because EndUTC is set (docs/ARCHITECTURE.md §5.1) — independent invariant.
        await Assert.ThrowsAsync<RecordLockedException>(() => recordingService.UpdateExecutionRunFactsAsync(
            execution.ExecutionID, new UpdateExecutionRunFactsRequest { InterventionCount = 2, OutcomeNotes = "Trying to change this." }));
    }

    private async Task<int> CreateScoreRuleAsync(ExperimentDesignService designService, long experimentVersionId)
    {
        var rule = await designService.CreateScoreRuleAsync(new CreateScoreRuleRequest
        {
            ExperimentVersionID = experimentVersionId,
            PhaseID = fixture.PhaseId,
            ScoreCategoryID = fixture.ScoreCategoryId,
            WeightPercent = 100m,
            MinScore = 0,
            MaxScore = 5,
            Active = true,
        });
        return rule.ScoreRuleID;
    }
}
