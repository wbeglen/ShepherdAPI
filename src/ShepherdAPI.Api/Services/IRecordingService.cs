using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Services;

public interface IRecordingService
{
    Task<IReadOnlyList<Execution>> GetExecutionsAsync(long iterationId);
    Task<Execution> GetExecutionAsync(long id);
    Task<Execution> CreateExecutionAsync(CreateExecutionRequest request);
    Task<Execution> UpdateExecutionRunFactsAsync(long id, UpdateExecutionRunFactsRequest request);

    Task<IReadOnlyList<Artifact>> GetArtifactsAsync(long executionId);
    Task<Artifact> GetArtifactAsync(long id);
    Task<Artifact> CreateArtifactAsync(CreateArtifactRequest request);

    Task<IReadOnlyList<ScoreObservation>> GetScoreObservationsAsync(long executionId);
    Task<ScoreObservation> GetScoreObservationAsync(long id);
    Task<ScoreObservation> CreateScoreObservationAsync(CreateScoreObservationRequest request);
    Task<ScoreObservation> UpdateScoreObservationContentAsync(long id, UpdateScoreObservationContentRequest request);

    Task<IReadOnlyList<ScoreObservationEvidence>> GetEvidenceAsync(long scoreObservationId);
    Task<ScoreObservationEvidence> AddEvidenceAsync(long scoreObservationId, CreateScoreObservationEvidenceRequest request);
    Task RemoveEvidenceAsync(long scoreObservationId, long artifactId);
}
