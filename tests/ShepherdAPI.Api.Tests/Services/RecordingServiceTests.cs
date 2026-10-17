using Moq;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;
using Xunit;

namespace ShepherdAPI.Api.Tests.Services;

/// <summary>Fast, DB-free tests for validation logic that doesn't depend on real SQL Server behavior.</summary>
public class RecordingServiceTests
{
    private readonly Mock<IRecordingRepository> _repository = new();
    private readonly Mock<IExperimentDesignRepository> _designRepository = new();
    private readonly Mock<ILockGuardQueries> _lockGuard = new();
    private readonly RecordingService _sut;

    public RecordingServiceTests()
    {
        _sut = new RecordingService(_repository.Object, _designRepository.Object, _lockGuard.Object);
    }

    [Fact]
    public async Task UpdateExecutionRunFactsAsync_ThrowsRecordLocked_WhenEndUtcAlreadySet()
    {
        var execution = new Execution { ExecutionID = 1, StartUTC = DateTime.UtcNow.AddHours(-1), EndUTC = DateTime.UtcNow };
        _repository.Setup(r => r.GetExecutionAsync(1)).ReturnsAsync(execution);
        _lockGuard.Setup(l => l.GetLockedUtcByExecutionIdAsync(1)).ReturnsAsync((DateTime?)null);

        await Assert.ThrowsAsync<RecordLockedException>(() => _sut.UpdateExecutionRunFactsAsync(1, new UpdateExecutionRunFactsRequest()));
        _repository.Verify(r => r.UpdateExecutionRunFactsAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Fact]
    public async Task UpdateExecutionRunFactsAsync_ThrowsRecordLocked_WhenGoverningExperimentVersionLocked()
    {
        var execution = new Execution { ExecutionID = 1, StartUTC = DateTime.UtcNow.AddHours(-1), EndUTC = null };
        _repository.Setup(r => r.GetExecutionAsync(1)).ReturnsAsync(execution);
        _lockGuard.Setup(l => l.GetLockedUtcByExecutionIdAsync(1)).ReturnsAsync(DateTime.UtcNow);

        await Assert.ThrowsAsync<RecordLockedException>(() => _sut.UpdateExecutionRunFactsAsync(1, new UpdateExecutionRunFactsRequest()));
    }

    [Fact]
    public async Task UpdateExecutionRunFactsAsync_RejectsEndUtcBeforeStartUtc()
    {
        var start = DateTime.UtcNow;
        var execution = new Execution { ExecutionID = 1, StartUTC = start, EndUTC = null };
        _repository.Setup(r => r.GetExecutionAsync(1)).ReturnsAsync(execution);
        _lockGuard.Setup(l => l.GetLockedUtcByExecutionIdAsync(1)).ReturnsAsync((DateTime?)null);

        var request = new UpdateExecutionRunFactsRequest { EndUTC = start.AddSeconds(-1) };

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.UpdateExecutionRunFactsAsync(1, request));
    }

    [Fact]
    public async Task AddEvidenceAsync_RejectsArtifactFromDifferentExecution()
    {
        var observation = new ScoreObservation { ScoreObservationID = 1, ExecutionID = 10 };
        var artifact = new Artifact { ArtifactID = 5, ExecutionID = 99 };
        _repository.Setup(r => r.GetScoreObservationAsync(1)).ReturnsAsync(observation);
        _lockGuard.Setup(l => l.GetLockedUtcByScoreObservationIdAsync(1)).ReturnsAsync((DateTime?)null);
        _repository.Setup(r => r.GetArtifactAsync(5)).ReturnsAsync(artifact);

        await Assert.ThrowsAsync<ValidationAppException>(() => _sut.AddEvidenceAsync(1, new CreateScoreObservationEvidenceRequest { ArtifactID = 5 }));
        _repository.Verify(r => r.CreateEvidenceLinkAsync(It.IsAny<ScoreObservationEvidence>()), Times.Never);
    }

    [Fact]
    public async Task CreateExecutionAsync_ThrowsNotFound_WhenTheIterationDoesNotExist_WithoutCallingTheProcedure()
    {
        _designRepository.Setup(r => r.GetIterationAsync(5)).ReturnsAsync((Iteration?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateExecutionAsync(new CreateExecutionRequest { IterationID = 5 }));
        _repository.Verify(r => r.CreateExecutionAsync(It.IsAny<Execution>()), Times.Never);
    }

    [Fact]
    public async Task CreateExecutionAsync_ThrowsNotFound_WhenTheProcedureReportsTheIterationGone()
    {
        _designRepository.Setup(r => r.GetIterationAsync(5)).ReturnsAsync(new Iteration { IterationID = 5, ExperimentVersionID = 1 });
        _repository.Setup(r => r.CreateExecutionAsync(It.IsAny<Execution>())).ReturnsAsync((long?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateExecutionAsync(new CreateExecutionRequest { IterationID = 5 }));
    }

    [Fact]
    public async Task CreateExecutionAsync_NoLongerChecksTheLockInTheServiceLayer_TheProcedureOwnsIt()
    {
        _designRepository.Setup(r => r.GetIterationAsync(5)).ReturnsAsync(new Iteration { IterationID = 5, ExperimentVersionID = 1 });
        _repository.Setup(r => r.CreateExecutionAsync(It.IsAny<Execution>())).ReturnsAsync(42L);
        _repository.Setup(r => r.GetExecutionAsync(42)).ReturnsAsync(new Execution { ExecutionID = 42, IterationID = 5 });

        var created = await _sut.CreateExecutionAsync(new CreateExecutionRequest { IterationID = 5 });

        Assert.Equal(42, created.ExecutionID);
        _lockGuard.Verify(l => l.GetLockedUtcByIterationIdAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task CreateScoreObservationAsync_ThrowsNotFound_WhenTheExecutionDoesNotExist_WithoutCallingTheProcedure()
    {
        _repository.Setup(r => r.GetExecutionAsync(10)).ReturnsAsync((Execution?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateScoreObservationAsync(new CreateScoreObservationRequest { ExecutionID = 10, ScoreRuleID = 1, RaterID = 1, ObservedScore = 3 }));
        _repository.Verify(r => r.CreateScoreObservationAsync(It.IsAny<ScoreObservation>()), Times.Never);
    }

    [Fact]
    public async Task CreateScoreObservationAsync_ThrowsNotFound_WhenTheProcedureReportsTheExecutionGone()
    {
        _repository.Setup(r => r.GetExecutionAsync(10)).ReturnsAsync(new Execution { ExecutionID = 10 });
        _repository.Setup(r => r.CreateScoreObservationAsync(It.IsAny<ScoreObservation>())).ReturnsAsync((long?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateScoreObservationAsync(new CreateScoreObservationRequest { ExecutionID = 10, ScoreRuleID = 1, RaterID = 1, ObservedScore = 3 }));
    }

    [Fact]
    public async Task CreateScoreObservationAsync_LeavesTheLockToTheProcedure_AndReturnsTheStoredRow()
    {
        _repository.Setup(r => r.GetExecutionAsync(10)).ReturnsAsync(new Execution { ExecutionID = 10 });
        _repository.Setup(r => r.CreateScoreObservationAsync(It.IsAny<ScoreObservation>())).ReturnsAsync(77L);
        _repository.Setup(r => r.GetScoreObservationAsync(77)).ReturnsAsync(new ScoreObservation { ScoreObservationID = 77, ExecutionID = 10 });

        var created = await _sut.CreateScoreObservationAsync(new CreateScoreObservationRequest { ExecutionID = 10, ScoreRuleID = 1, RaterID = 1, ObservedScore = 3 });

        Assert.Equal(77, created.ScoreObservationID);
        _lockGuard.Verify(l => l.GetLockedUtcByExecutionIdAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task UpdateScoreObservationContentAsync_ThrowsNotFound_WhenTheObservationDoesNotExist_WithoutCallingTheProcedure()
    {
        _repository.Setup(r => r.GetScoreObservationAsync(5)).ReturnsAsync((ScoreObservation?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.UpdateScoreObservationContentAsync(5, new UpdateScoreObservationContentRequest { ObservedScore = 3 }));
        _repository.Verify(r => r.UpdateScoreObservationContentAsync(It.IsAny<long>(), It.IsAny<decimal>(), It.IsAny<DateTime?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task UpdateScoreObservationContentAsync_ThrowsNotFound_WhenTheProcedureReportsItGone()
    {
        _repository.Setup(r => r.GetScoreObservationAsync(5)).ReturnsAsync(new ScoreObservation { ScoreObservationID = 5 });
        _repository.Setup(r => r.UpdateScoreObservationContentAsync(5, 3m, null, null)).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.UpdateScoreObservationContentAsync(5, new UpdateScoreObservationContentRequest { ObservedScore = 3 }));
    }

    [Fact]
    public async Task UpdateScoreObservationContentAsync_LeavesTheLockToTheProcedure()
    {
        _repository.Setup(r => r.GetScoreObservationAsync(5)).ReturnsAsync(new ScoreObservation { ScoreObservationID = 5, ObservedScore = 4m });
        _repository.Setup(r => r.UpdateScoreObservationContentAsync(5, 4m, null, null)).ReturnsAsync(true);

        var updated = await _sut.UpdateScoreObservationContentAsync(5, new UpdateScoreObservationContentRequest { ObservedScore = 4 });

        Assert.Equal(4m, updated.ObservedScore);
        _lockGuard.Verify(l => l.GetLockedUtcByScoreObservationIdAsync(It.IsAny<long>()), Times.Never);
    }
}
