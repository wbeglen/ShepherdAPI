using Microsoft.Data.SqlClient;
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
    IExperimentDesignRepository designRepository) : IRecordingService
{
    // -- Execution --------------------------------------------------------------

    public Task<IReadOnlyList<Execution>> GetExecutionsAsync(long iterationId) => repository.GetExecutionsByIterationAsync(iterationId);

    public async Task<Execution> GetExecutionAsync(long id) =>
        await repository.GetExecutionAsync(id) ?? throw NotFound("Execution", id);

    /// <summary>
    /// Order of checks (Issue #1): iteration not found (404), then version locked (50100 version-locked),
    /// then the phase's rule configuration (50200 invalid-rule-configuration). The lock and rule checks
    /// run inside dbo.usp_CreateExecution, atomically with the insert.
    /// </summary>
    public async Task<Execution> CreateExecutionAsync(CreateExecutionRequest request)
    {
        var iteration = await designRepository.GetIterationAsync(request.IterationID) ?? throw NotFound("Iteration", request.IterationID);

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
        long? id;
        try
        {
            id = await repository.CreateExecutionAsync(execution);
        }
        catch (SqlException ex) when (ex.Number == ErrorCatalog.InvalidRuleConfiguration.SqlNumber)
        {
            // The procedure has already rolled back; read the rules afterwards purely to explain the refusal.
            var activeRules = await designRepository.GetScoreRulesByExperimentVersionAsync(iteration.ExperimentVersionID);
            throw new CatalogException(
                ErrorCatalog.InvalidRuleConfiguration,
                ex.Message,
                ExecutionRuleConfiguration.Describe(iteration.ExperimentVersionID, request.PhaseID, activeRules));
        }

        // Null means the iteration vanished between the lookup above and the procedure's own check.
        return await GetExecutionAsync(id ?? throw NotFound("Iteration", request.IterationID));
    }

    public async Task<Execution> UpdateExecutionRunFactsAsync(long id, UpdateExecutionRunFactsRequest request)
    {
        // The pre-read supplies the 404 and the immutable StartUTC. The lock and "EndUTC is set" restrictions are
        // checked inside dbo.usp_UpdateExecutionRunFacts, under the parent-version lock (version-locked,
        // execution-run-facts-frozen).
        var execution = await GetExecutionAsync(id);

        if (request.EndUTC is not null && request.EndUTC < execution.StartUTC)
        {
            throw new ValidationAppException(nameof(request.EndUTC), "EndUTC must be greater than or equal to StartUTC.");
        }

        if (request.InterventionCount < 0)
        {
            throw new ValidationAppException(nameof(request.InterventionCount), "InterventionCount must be greater than or equal to zero.");
        }

        var found = await repository.UpdateExecutionRunFactsAsync(
            id, request.ModelUsed, request.InterfaceUsed, request.InterventionCount, request.OutcomeNotes, request.EndUTC);
        if (!found)
        {
            throw NotFound("Execution", id);
        }

        return await GetExecutionAsync(id);
    }

    // -- Artifact -----------------------------------------------------------------

    public Task<IReadOnlyList<Artifact>> GetArtifactsAsync(long executionId) => repository.GetArtifactsByExecutionAsync(executionId);

    public async Task<Artifact> GetArtifactAsync(long id) =>
        await repository.GetArtifactAsync(id) ?? throw NotFound("Artifact", id);

    public async Task<Artifact> CreateArtifactAsync(CreateArtifactRequest request)
    {
        await GetExecutionAsync(request.ExecutionID); // 404 if the Execution doesn't exist. The lock is checked in dbo.usp_CreateArtifact.

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

        // Null means the Execution vanished between the lookup above and the procedure's own check.
        return await GetArtifactAsync(id ?? throw NotFound("Execution", request.ExecutionID));
    }

    // -- ScoreObservation -----------------------------------------------------------

    public Task<IReadOnlyList<ScoreObservation>> GetScoreObservationsAsync(long executionId) => repository.GetScoreObservationsByExecutionAsync(executionId);

    public async Task<ScoreObservation> GetScoreObservationAsync(long id) =>
        await repository.GetScoreObservationAsync(id) ?? throw NotFound("ScoreObservation", id);

    /// <summary>
    /// The lock, context (version, phase) and range checks run inside
    /// dbo.usp_CreateScoreObservation, atomically with the insert; their failures surface as the catalog's
    /// version-locked, version-phase-mismatch and out-of-range-score errors, and a duplicate as
    /// duplicate-observation. Incomplete phases, unfinished Executions and missing observations never block a write.
    /// </summary>
    public async Task<ScoreObservation> CreateScoreObservationAsync(CreateScoreObservationRequest request)
    {
        await GetExecutionAsync(request.ExecutionID); // 404 if the Execution doesn't exist.

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

        // Null means the Execution vanished between the lookup above and the procedure's own check.
        return await GetScoreObservationAsync(id ?? throw NotFound("Execution", request.ExecutionID));
    }

    /// <summary>Content-only correction (ObservedScore, ObservedUTC, ObservationNotes); the lock and range checks run in dbo.usp_UpdateScoreObservationContent.</summary>
    public async Task<ScoreObservation> UpdateScoreObservationContentAsync(long id, UpdateScoreObservationContentRequest request)
    {
        await GetScoreObservationAsync(id); // 404 if the observation doesn't exist.

        var found = await repository.UpdateScoreObservationContentAsync(id, request.ObservedScore, request.ObservedUTC, request.ObservationNotes);
        if (!found)
        {
            throw NotFound("ScoreObservation", id);
        }
        return await GetScoreObservationAsync(id);
    }

    // -- ScoreObservationEvidence -----------------------------------------------------

    public Task<IReadOnlyList<ScoreObservationEvidence>> GetEvidenceAsync(long scoreObservationId) => repository.GetEvidenceByScoreObservationAsync(scoreObservationId);

    public async Task<ScoreObservationEvidence> AddEvidenceAsync(long scoreObservationId, CreateScoreObservationEvidenceRequest request)
    {
        var observation = await GetScoreObservationAsync(scoreObservationId);
        var artifact = await GetArtifactAsync(request.ArtifactID);

        // Both Executions are immutable, so this check cannot race and stays here; the lock is checked, with both
        // owning versions locked, in dbo.usp_AddEvidence.
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
        if (!await repository.CreateEvidenceLinkAsync(link))
        {
            throw NotFound("ScoreObservationEvidence", $"{scoreObservationId}/{request.ArtifactID}");
        }

        return link;
    }

    public async Task RemoveEvidenceAsync(long scoreObservationId, long artifactId)
    {
        await GetScoreObservationAsync(scoreObservationId);

        var removed = await repository.DeleteEvidenceLinkAsync(scoreObservationId, artifactId);
        if (!removed)
        {
            throw NotFound("ScoreObservationEvidence", $"{scoreObservationId}/{artifactId}");
        }
    }

    // -- helpers ------------------------------------------------------------------

    private static NotFoundException NotFound(string entity, object id) => new($"{entity} {id} was not found.");
}
