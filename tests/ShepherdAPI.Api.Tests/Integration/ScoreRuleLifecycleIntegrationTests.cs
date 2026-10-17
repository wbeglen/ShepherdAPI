using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;
using Xunit;

namespace ShepherdAPI.Api.Tests.Integration;

/// <summary>
/// Verifies ScoreRule's add/delete-only lifecycle (RowState 'A'/'D') against a real SQL Server
/// database: Active-only listing, database-default creation, soft deletion, historical by-ID
/// resolution, and that existing ScoreObservation references to a retired rule keep working.
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

    private CreateScoreRuleRequest NewRuleRequest(long experimentVersionId, int? scoreCategoryId = null, decimal weight = 50m) => new()
    {
        ExperimentVersionID = experimentVersionId,
        PhaseID = fixture.PhaseId,
        ScoreCategoryID = scoreCategoryId ?? fixture.ScoreCategoryId,
        WeightPercent = weight,
        MinScore = 0,
        MaxScore = 5,
        Active = true,
    };

    [Fact]
    public async Task CreateScoreRuleAsync_DefaultsToActiveRowState()
    {
        var designService = CreateDesignService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);

        var created = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId));

        Assert.Equal(RowState.Active.ToString(), created.RowState);
    }

    [Fact]
    public async Task GetScoreRulesAsync_ReturnsOnlyActiveRowsForTheExperimentVersion()
    {
        var designService = CreateDesignService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);

        var keep = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId));
        var retire = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId, fixture.ScoreCategoryId2));

        await designService.DeleteScoreRuleAsync(retire.ScoreRuleID);

        var active = await designService.GetScoreRulesAsync(experimentVersionId);

        Assert.Contains(active, r => r.ScoreRuleID == keep.ScoreRuleID);
        Assert.DoesNotContain(active, r => r.ScoreRuleID == retire.ScoreRuleID);
        Assert.All(active, r => Assert.Equal(RowState.Active.ToString(), r.RowState));
    }

    [Fact]
    public async Task DeleteScoreRuleAsync_SoftDeletes_RowStillResolvableById()
    {
        var designService = CreateDesignService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);
        var rule = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId));

        await designService.DeleteScoreRuleAsync(rule.ScoreRuleID);

        // Not a physical delete: GetScoreRuleAsync (historical/internal resolution) still finds it,
        // now marked 'D' rather than gone.
        var resolved = await designService.GetScoreRuleAsync(rule.ScoreRuleID);
        Assert.Equal(RowState.Deleted.ToString(), resolved.RowState);
    }

    [Fact]
    public async Task DeleteScoreRuleAsync_IsIdempotent_WhenCalledTwice()
    {
        var designService = CreateDesignService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);
        var rule = await designService.CreateScoreRuleAsync(NewRuleRequest(experimentVersionId));

        await designService.DeleteScoreRuleAsync(rule.ScoreRuleID);
        await designService.DeleteScoreRuleAsync(rule.ScoreRuleID); // must not throw

        var resolved = await designService.GetScoreRuleAsync(rule.ScoreRuleID);
        Assert.Equal(RowState.Deleted.ToString(), resolved.RowState);
    }

    [Fact]
    public async Task RetiringAScoreRule_PreservesExistingScoreObservationReferences()
    {
        var designService = CreateDesignService();
        var recordingService = CreateRecordingService();
        var experimentVersionId = await CreateExperimentVersionAsync(designService);
        // The phase's active rules must total 100.00 before an Execution can be created (Issue #1).
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

        // Retire the rule after it has already been used for scoring.
        await designService.DeleteScoreRuleAsync(rule.ScoreRuleID);

        // The existing observation, and its reference to the now-retired rule, remain fully valid.
        var reloadedObservation = await recordingService.GetScoreObservationAsync(observation.ScoreObservationID);
        Assert.Equal(rule.ScoreRuleID, reloadedObservation.ScoreRuleID);

        var reloadedRule = await designService.GetScoreRuleAsync(reloadedObservation.ScoreRuleID);
        Assert.Equal(RowState.Deleted.ToString(), reloadedRule.RowState);
        Assert.Equal(rule.WeightPercent, reloadedRule.WeightPercent);
    }
}
