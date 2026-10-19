using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Services;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Verifies ScoreRule's lifecycle (Issue #7) against a real SQL Server database: rules are created, updated and
/// physically deleted while the version is writable, and once the first Execution exists every rule an
/// observation refers to stays exactly as it was used.
/// </summary>
[Collection("Database")]
public class ScoreRuleLifecycleIntegrationTests(DatabaseFixture fixture)
{
    private ExperimentDesignService CreateDesignService() =>
        new(new ExperimentDesignRepository(fixture.ConnectionFactory));

    private RecordingService CreateRecordingService() =>
        new(new RecordingRepository(fixture.ConnectionFactory),
            new ExperimentDesignRepository(fixture.ConnectionFactory),
            new LockGuardQueries(fixture.ConnectionFactory));

    private async Task<long> CreateExperimentVersionAsync(ExperimentDesignService designService) =>
        (await designService.CreateExperimentVersionAsync(new CreateExperimentVersionRequest
        {
            Name = $"ScoreRuleTest-{Guid.NewGuid():N}",
            VersionLabel = "v1",
            StatusID = fixture.DraftStatusId,
            EffectiveDateUTC = DateTime.UtcNow,
            Description = "ScoreRule lifecycle test.",
        })).ExperimentVersionID;

    private CreateScoreRuleRequest NewRuleRequest(long experimentVersionId, int? scoreCategoryId = null, decimal weight = 50m, bool active = true) => new()
    {
        ExperimentVersionID = experimentVersionId,
        PhaseID = fixture.PhaseId,
        ScoreCategoryID = scoreCategoryId ?? fixture.ScoreCategoryId,
        WeightPercent = weight,
        MinScore = 0,
        MaxScore = 5,
        Active = active,
    };

    [Fact]
    public async Task GetScoreRulesAsync_ReturnsEveryRuleOfTheExperimentVersion_AndNoDeletedOnes()
    {
        var designService = CreateDesignService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);

        var keep = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId));
        var inactive = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId, fixture.ScoreCategoryId2, active: false));
        var otherVersion = await CreateExperimentVersionAsync(designService);
        await designService.CreateScoreRuleAsync(NewRuleRequest(otherVersion));

        // ScoreRule.Active plays no part in listing.
        var rules = await designService.GetScoreRulesAsync(experimentVersionId);
        Assert.Equal([keep.ScoreRuleID, inactive.ScoreRuleID], rules.Select(r => r.ScoreRuleID).Order());

        await designService.DeleteScoreRuleAsync(inactive.ScoreRuleID);
        Assert.Equal([keep.ScoreRuleID], (await designService.GetScoreRulesAsync(experimentVersionId)).Select(r => r.ScoreRuleID));
    }

    [Fact]
    public async Task UpdateScoreRuleAsync_ReplacesTheNonKeyFields()
    {
        var designService = CreateDesignService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);
        var rule = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId));

        var updated = await designService.UpdateScoreRuleAsync(rule.ScoreRuleID, new UpdateScoreRuleRequest
        {
            WeightPercent = 75.5m, MinScore = -2, MaxScore = 8, Active = false, RuleNotes = "corrected",
        });

        Assert.Equal((rule.ScoreRuleID, experimentVersionId, fixture.PhaseId, fixture.ScoreCategoryId),
                     (updated.ScoreRuleID, updated.ExperimentVersionID, updated.PhaseID, updated.ScoreCategoryID));
        Assert.Equal((75.5m, -2, 8, false, "corrected"), (updated.WeightPercent, updated.MinScore, updated.MaxScore, updated.Active, updated.RuleNotes));
    }

    [Fact]
    public async Task DeleteScoreRuleAsync_IsAPhysicalDelete_ASecondDeleteIsNotFound()
    {
        var designService = CreateDesignService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);
        var rule = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId));

        await designService.DeleteScoreRuleAsync(rule.ScoreRuleID);

        await Assert.ThrowsAsync<NotFoundException>(() => designService.GetScoreRuleAsync(rule.ScoreRuleID));
        await Assert.ThrowsAsync<NotFoundException>(() => designService.DeleteScoreRuleAsync(rule.ScoreRuleID));
    }

    [Fact]
    public async Task ADeletedDraftRule_NoLongerOccupiesItsVersionPhaseCategoryCombination()
    {
        var designService = CreateDesignService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);
        var original = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId, weight: 30m));

        await designService.DeleteScoreRuleAsync(original.ScoreRuleID);
        var replacement = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId, weight: 100m));

        Assert.NotEqual(original.ScoreRuleID, replacement.ScoreRuleID);
        Assert.Equal(100m, replacement.WeightPercent);
    }

    [Fact]
    public async Task AfterTheFirstExecution_TheRuleAndItsObservationReferenceAreFixed_ThroughFinalization()
    {
        var designService = CreateDesignService();
        var recordingService = CreateRecordingService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);
        // The phase's rules must total 100.00 before an Execution can be created (Issue #1).
        var rule = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId, weight: 100m));

        var iteration = await designService.CreateIterationAsync(new CreateIterationRequest
        {
            ExperimentVersionID = experimentVersionId, Sequence = 1, IterationLabel = "Iteration 1", Prompt = "Do the thing.",
        });
        var execution = await recordingService.CreateExecutionAsync(new CreateExecutionRequest
        {
            IterationID = iteration.IterationID, AgentID = fixture.AgentId, PhaseID = fixture.PhaseId,
            StatusID = fixture.DraftStatusId, StartUTC = DateTime.UtcNow, InterventionCount = 0, AttemptNumber = 1,
        });
        var observation = await recordingService.CreateScoreObservationAsync(new CreateScoreObservationRequest
        {
            ExecutionID = execution.ExecutionID, ScoreRuleID = rule.ScoreRuleID, RaterID = fixture.RaterId, ObservedScore = 4,
        });
        var change = new UpdateScoreRuleRequest { WeightPercent = 10m, MinScore = 0, MaxScore = 5 };

        async Task<int> Rejected(Func<Task> write) => (await Assert.ThrowsAsync<SqlException>(write)).Number;

        // Live (has an Execution, unlocked): design-frozen.
        Assert.Equal(ErrorCatalog.DesignFrozen.SqlNumber, await Rejected(() => designService.UpdateScoreRuleAsync(rule.ScoreRuleID, change)));
        Assert.Equal(ErrorCatalog.DesignFrozen.SqlNumber, await Rejected(() => designService.DeleteScoreRuleAsync(rule.ScoreRuleID)));

        // Final (locked): version-locked.
        await recordingService.UpdateExecutionRunFactsAsync(execution.ExecutionID, new UpdateExecutionRunFactsRequest { EndUTC = DateTime.UtcNow });
        await designService.LockExperimentVersionAsync(experimentVersionId, new LockExperimentVersionRequest());
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, await Rejected(() => designService.UpdateScoreRuleAsync(rule.ScoreRuleID, change)));
        Assert.Equal(ErrorCatalog.VersionLocked.SqlNumber, await Rejected(() => designService.DeleteScoreRuleAsync(rule.ScoreRuleID)));

        // The observation still refers to the rule, which is still listed exactly as it was used.
        var reloadedObservation = await recordingService.GetScoreObservationAsync(observation.ScoreObservationID);
        Assert.Equal(rule.ScoreRuleID, reloadedObservation.ScoreRuleID);
        var listed = Assert.Single(await designService.GetScoreRulesAsync(experimentVersionId));
        Assert.Equal((rule.ScoreRuleID, rule.WeightPercent, rule.MinScore, rule.MaxScore), (listed.ScoreRuleID, listed.WeightPercent, listed.MinScore, listed.MaxScore));
    }
}
