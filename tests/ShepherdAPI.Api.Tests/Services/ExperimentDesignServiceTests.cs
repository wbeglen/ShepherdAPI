using Moq;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;
using Xunit;

namespace ShepherdAPI.Api.Tests.Services;

/// <summary>Fast, DB-free tests for validation logic that doesn't depend on real SQL Server behavior.</summary>
public class ExperimentDesignServiceTests
{
    private readonly Mock<IExperimentDesignRepository> _repository = new();
    private readonly ExperimentDesignService _sut;

    public ExperimentDesignServiceTests()
    {
        _sut = new ExperimentDesignService(_repository.Object);
    }

    [Fact]
    public async Task CreateScoreRuleAsync_RejectsMinScoreNotLessThanMaxScore()
    {
        var version = new ExperimentVersion { ExperimentVersionID = 1, LockedUTC = null };
        _repository.Setup(r => r.GetExperimentVersionAsync(1)).ReturnsAsync(version);

        var request = new CreateScoreRuleRequest { ExperimentVersionID = 1, PhaseID = 1, ScoreCategoryID = 1, MinScore = 5, MaxScore = 5, WeightPercent = 10 };

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.CreateScoreRuleAsync(request));
        _repository.Verify(r => r.CreateScoreRuleAsync(It.IsAny<ScoreRule>()), Times.Never);
    }

    [Fact]
    public async Task UpdateExperimentVersionAsync_LeavesTheLockAndFreezeChecksToTheProcedure()
    {
        var version = new ExperimentVersion { ExperimentVersionID = 1, LockedUTC = DateTime.UtcNow };
        _repository.Setup(r => r.GetExperimentVersionAsync(1)).ReturnsAsync(version);
        _repository.Setup(r => r.UpdateExperimentVersionAsync(1, "x", "v1", It.IsAny<DateTime>(), "d")).ReturnsAsync(true);

        var request = new UpdateExperimentVersionRequest { Name = "x", VersionLabel = "v1", Description = "d" };
        var updated = await _sut.UpdateExperimentVersionAsync(1, request);

        // The service no longer pre-checks LockedUTC: a stale read here could not be trusted, so the
        // procedure decides under the parent-version lock (and throws the catalog errors itself).
        Assert.Equal(1, updated.ExperimentVersionID);
        _repository.Verify(r => r.UpdateExperimentVersionAsync(1, "x", "v1", It.IsAny<DateTime>(), "d"), Times.Once);
    }

    [Fact]
    public async Task UpdateExperimentVersionAsync_ThrowsNotFound_WhenTheProcedureReportsItGone()
    {
        _repository.Setup(r => r.UpdateExperimentVersionAsync(7, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.UpdateExperimentVersionAsync(7, new UpdateExperimentVersionRequest { Name = "x", VersionLabel = "v1", Description = "d" }));
    }

    [Fact]
    public async Task CreateIterationAsync_ThrowsNotFound_WhenTheProcedureReportsTheVersionGone()
    {
        _repository.Setup(r => r.CreateIterationAsync(It.IsAny<Iteration>())).ReturnsAsync((long?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateIterationAsync(new CreateIterationRequest { ExperimentVersionID = 9, Sequence = 1, IterationLabel = "i", Prompt = "p" }));
    }

    [Fact]
    public async Task DeleteScoreRuleAsync_IsTheSoftDelete_AndThrowsNotFoundOnlyWhenTheRuleDoesNotExist()
    {
        _repository.Setup(r => r.SoftDeleteScoreRuleAsync(5)).ReturnsAsync(true);
        _repository.Setup(r => r.SoftDeleteScoreRuleAsync(6)).ReturnsAsync(false);

        await _sut.DeleteScoreRuleAsync(5);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteScoreRuleAsync(6));

        _repository.Verify(r => r.GetExperimentVersionAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task LockExperimentVersionAsync_Finalizes_IgnoringTheOptionalStatusId()
    {
        _repository.Setup(r => r.FinalizeExperimentVersionAsync(1)).ReturnsAsync(DateTime.UtcNow);
        _repository.Setup(r => r.GetExperimentVersionAsync(1)).ReturnsAsync(new ExperimentVersion { ExperimentVersionID = 1, StatusID = 5, LockedUTC = DateTime.UtcNow });

        var withStatus = await _sut.LockExperimentVersionAsync(1, new LockExperimentVersionRequest { StatusID = 2 });
        var without = await _sut.LockExperimentVersionAsync(1, new LockExperimentVersionRequest());

        Assert.Equal(5, withStatus.StatusID);
        Assert.Equal(5, without.StatusID);
        _repository.Verify(r => r.SetExperimentVersionStatusAsync(It.IsAny<long>(), It.IsAny<int>()), Times.Never);
        _repository.Verify(r => r.FinalizeExperimentVersionAsync(1), Times.Exactly(2));
    }

    [Fact]
    public async Task LockExperimentVersionAsync_ThrowsNotFound_WhenTheVersionDoesNotExist()
    {
        _repository.Setup(r => r.FinalizeExperimentVersionAsync(9)).ReturnsAsync((DateTime?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.LockExperimentVersionAsync(9, new LockExperimentVersionRequest()));
    }

    [Fact]
    public async Task SetExperimentVersionStatusAsync_AllowedEvenWhenLocked()
    {
        var version = new ExperimentVersion { ExperimentVersionID = 1, LockedUTC = DateTime.UtcNow, StatusID = 2 };
        _repository.Setup(r => r.GetExperimentVersionAsync(1)).ReturnsAsync(version);
        _repository.Setup(r => r.SetExperimentVersionStatusAsync(1, 3)).ReturnsAsync(true);

        await _sut.SetExperimentVersionStatusAsync(1, new SetExperimentVersionStatusRequest { StatusID = 3 });

        _repository.Verify(r => r.SetExperimentVersionStatusAsync(1, 3), Times.Once);
    }

    [Fact]
    public async Task GetExperimentVersionAsync_ThrowsNotFound_WhenMissing()
    {
        _repository.Setup(r => r.GetExperimentVersionAsync(99)).ReturnsAsync((ExperimentVersion?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetExperimentVersionAsync(99));
    }
}
