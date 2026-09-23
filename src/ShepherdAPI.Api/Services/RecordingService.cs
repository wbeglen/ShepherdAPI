using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Services;

/// <summary>
/// Business rules for the recording aggregate: Execution, Artifact, ScoreObservation, and
/// ScoreObservationEvidence. Two independent invariants apply here, per docs/ARCHITECTURE.md §5:
///   - §5.1: an Execution's own run-facts freeze once its EndUTC is set, regardless of the
///     ExperimentVersion lock.
///   - §5.3: once the governing ExperimentVersion is locked, ALL data in this aggregate for that
///     version is frozen — mutation and creation both — with no per-Execution exception.
/// </summary>
public class RecordingService(
    IRecordingRepository repository,
    IExperimentDesignRepository designRepository,
    ILockGuardQueries lockGuard) : IRecordingService
{
    // -- Execution --------------------------------------------------------------

    public Task<IReadOnlyList<Execution>> GetExecutionsAsync(long iterationId) => repository.GetExecutionsByIterationAsync(iterationId);

    public async Task<Execution> GetExecutionAsync(long id) =>
        await repository.GetExecutionAsync(id) ?? throw NotFound("Execution", id);

    public async Task<Execution> CreateExecutionAsync(CreateExecutionRequest request)
    {
        _ = await designRepository.GetIterationAsync(request.IterationID) ?? throw NotFound("Iteration", request.IterationID);
        var iterationLockedUtc = await lockGuard.GetLockedUtcByIterationIdAsync(request.IterationID);
        EnsureUnlocked(iterationLockedUtc, "Iteration", request.IterationID);

        var execution = new Execution
        {
            IterationID = request.IterationID,
            AgentID = request.AgentID,
            PhaseID = request.PhaseID,
            StatusID = request.StatusID,
            StartUTC = request.StartUTC,
            EndUTC = null,
            ModelUsed = request.ModelUsed,
            InterfaceUsed = request.InterfaceUsed,
            InterventionCount = request.InterventionCount,
            OutcomeNotes = request.OutcomeNotes,
            AttemptNumber = request.AttemptNumber,
        };
        var id = await repository.CreateExecutionAsync(execution);
        return await GetExecutionAsync(id);
    }

    public async Task<Execution> UpdateExecutionRunFactsAsync(long id, UpdateExecutionRunFactsRequest request)
    {
        var execution = await GetExecutionAsync(id);

        var lockedUtc = await lockGuard.GetLockedUtcByExecutionIdAsync(id);
        EnsureUnlocked(lockedUtc, "Execution", id);

        if (execution.EndUTC is not null)
        {
            throw new RecordLockedException(
                $"Execution {id} completed at {execution.EndUTC:O}; its run-facts are immutable through normal ShepherdAPI operations.");
        }

        if (request.EndUTC is not null && request.EndUTC < execution.StartUTC)
        {
            throw new ValidationAppException(nameof(request.EndUTC), "EndUTC must be greater than or equal to StartUTC.");
        }

        if (request.InterventionCount < 0)
        {
            throw new ValidationAppException(nameof(request.InterventionCount), "InterventionCount must be greater than or equal to zero.");
        }

        await repository.UpdateExecutionRunFactsAsync(
            id, request.ModelUsed, request.InterfaceUsed, request.InterventionCount, request.OutcomeNotes, request.EndUTC);
        return await GetExecutionAsync(id);
    }

    // -- Artifact -----------------------------------------------------------------

    public Task<IReadOnlyList<Artifact>> GetArtifactsAsync(long executionId) => repository.GetArtifactsByExecutionAsync(executionId);

    public async Task<Artifact> GetArtifactAsync(long id) =>
        await repository.GetArtifactAsync(id) ?? throw NotFound("Artifact", id);

    public async Task<Artifact> CreateArtifactAsync(CreateArtifactRequest request)
    {
        await GetExecutionAsync(request.ExecutionID); // 404 if the Execution doesn't exist.

        var lockedUtc = await lockGuard.GetLockedUtcByExecutionIdAsync(request.ExecutionID);
        EnsureUnlocked(lockedUtc, "Execution", request.ExecutionID);

        var artifact = new Artifact
        {
            ExecutionID = request.ExecutionID,
            ArtifactTypeID = request.ArtifactTypeID,
            OriginalName = request.OriginalName,
            OriginalURI = request.OriginalURI,
            StoredURI = request.StoredURI,
            MimeType = request.MimeType,
            ByteLength = request.ByteLength,
            SHA256 = request.SHA256.ToUpperInvariant(),
            ReceivedUTC = request.ReceivedUTC,
            StoredUTC = request.StoredUTC,
        };
        var id = await repository.CreateArtifactAsync(artifact);
        return await GetArtifactAsync(id);
    }

    // -- ScoreObservation -----------------------------------------------------------

    public Task<IReadOnlyList<ScoreObservation>> GetScoreObservationsAsync(long executionId) => repository.GetScoreObservationsByExecutionAsync(executionId);

    public async Task<ScoreObservation> GetScoreObservationAsync(long id) =>
        await repository.GetScoreObservationAsync(id) ?? throw NotFound("ScoreObservation", id);

    public async Task<ScoreObservation> CreateScoreObservationAsync(CreateScoreObservationRequest request)
    {
        await GetExecutionAsync(request.ExecutionID); // 404 if the Execution doesn't exist.

        var lockedUtc = await lockGuard.GetLockedUtcByExecutionIdAsync(request.ExecutionID);
        EnsureUnlocked(lockedUtc, "Execution", request.ExecutionID);

        var observation = new ScoreObservation
        {
            ExecutionID = request.ExecutionID,
            ScoreRuleID = request.ScoreRuleID,
            RaterID = request.RaterID,
            ObservedScore = request.ObservedScore,
            ObservedUTC = request.ObservedUTC,
            ObservationNotes = request.ObservationNotes,
        };
        var id = await repository.CreateScoreObservationAsync(observation);
        return await GetScoreObservationAsync(id);
    }

    public async Task<ScoreObservation> UpdateScoreObservationContentAsync(long id, UpdateScoreObservationContentRequest request)
    {
        await GetScoreObservationAsync(id);

        var lockedUtc = await lockGuard.GetLockedUtcByScoreObservationIdAsync(id);
        EnsureUnlocked(lockedUtc, "ScoreObservation", id);

        await repository.UpdateScoreObservationContentAsync(id, request.ObservedScore, request.ObservedUTC, request.ObservationNotes);
        return await GetScoreObservationAsync(id);
    }

    // -- ScoreObservationEvidence -----------------------------------------------------

    public Task<IReadOnlyList<ScoreObservationEvidence>> GetEvidenceAsync(long scoreObservationId) => repository.GetEvidenceByScoreObservationAsync(scoreObservationId);

    public async Task<ScoreObservationEvidence> AddEvidenceAsync(long scoreObservationId, CreateScoreObservationEvidenceRequest request)
    {
        var observation = await GetScoreObservationAsync(scoreObservationId);

        var lockedUtc = await lockGuard.GetLockedUtcByScoreObservationIdAsync(scoreObservationId);
        EnsureUnlocked(lockedUtc, "ScoreObservation", scoreObservationId);

        var artifact = await GetArtifactAsync(request.ArtifactID);
        if (artifact.ExecutionID != observation.ExecutionID)
        {
            throw new ValidationAppException(nameof(request.ArtifactID),
                "The Artifact must belong to the same Execution as the ScoreObservation it is evidencing.");
        }

        var link = new ScoreObservationEvidence
        {
            ScoreObservationID = scoreObservationId,
            ArtifactID = request.ArtifactID,
            EvidenceNotes = request.EvidenceNotes,
        };
        await repository.CreateEvidenceLinkAsync(link);
        return link;
    }

    public async Task RemoveEvidenceAsync(long scoreObservationId, long artifactId)
    {
        await GetScoreObservationAsync(scoreObservationId);

        var lockedUtc = await lockGuard.GetLockedUtcByScoreObservationIdAsync(scoreObservationId);
        EnsureUnlocked(lockedUtc, "ScoreObservation", scoreObservationId);

        var removed = await repository.DeleteEvidenceLinkAsync(scoreObservationId, artifactId);
        if (!removed)
        {
            throw NotFound("ScoreObservationEvidence", $"{scoreObservationId}/{artifactId}");
        }
    }

    // -- helpers ------------------------------------------------------------------

    private static void EnsureUnlocked(DateTime? lockedUtc, string ownerEntity, object ownerId)
    {
        if (lockedUtc is not null)
        {
            throw new RecordLockedException(
                $"The ExperimentVersion governing {ownerEntity} {ownerId} was locked at {lockedUtc:O}; its data is immutable through normal ShepherdAPI operations.");
        }
    }

    private static NotFoundException NotFound(string entity, object id) => new($"{entity} {id} was not found.");
}
