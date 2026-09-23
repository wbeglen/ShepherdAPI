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
    public async Task UpdateExperimentVersionAsync_ThrowsRecordLocked_WhenAlreadyLocked()
    {
        var lockedUtc = DateTime.UtcNow;
        var version = new ExperimentVersion { ExperimentVersionID = 1, LockedUTC = lockedUtc };
        _repository.Setup(r => r.GetExperimentVersionAsync(1)).ReturnsAsync(version);

        var request = new UpdateExperimentVersionRequest { Name = "x", VersionLabel = "v1", Description = "d" };

        await Assert.ThrowsAsync<RecordLockedException>(() => _sut.UpdateExperimentVersionAsync(1, request));
        _repository.Verify(r => r.UpdateExperimentVersionAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LockExperimentVersionAsync_ThrowsRecordLocked_WhenAlreadyLocked()
    {
        var version = new ExperimentVersion { ExperimentVersionID = 1, LockedUTC = DateTime.UtcNow };
        _repository.Setup(r => r.GetExperimentVersionAsync(1)).ReturnsAsync(version);

        await Assert.ThrowsAsync<RecordLockedException>(() => _sut.LockExperimentVersionAsync(1, new LockExperimentVersionRequest { StatusID = 2 }));
        _repository.Verify(r => r.LockExperimentVersionAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
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
