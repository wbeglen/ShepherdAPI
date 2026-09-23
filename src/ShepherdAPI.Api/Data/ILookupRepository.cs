using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Data;

/// <summary>
/// Data access for the six global reference tables (Agent, ArtifactType, Phase, Rater,
/// ScoreCategory, Status). None of these are governed by an ExperimentVersion lock — they are
/// shared vocabulary, not experimental record data.
/// </summary>
public interface ILookupRepository
{
    Task<IReadOnlyList<Agent>> GetAgentsAsync();
    Task<Agent?> GetAgentAsync(int id);
    Task<int> CreateAgentAsync(Agent agent);
    Task<bool> UpdateAgentAsync(Agent agent);

    Task<IReadOnlyList<ArtifactType>> GetArtifactTypesAsync();
    Task<ArtifactType?> GetArtifactTypeAsync(int id);
    Task<int> CreateArtifactTypeAsync(ArtifactType artifactType);
    Task<bool> UpdateArtifactTypeAsync(ArtifactType artifactType);

    Task<IReadOnlyList<Phase>> GetPhasesAsync();
    Task<Phase?> GetPhaseAsync(int id);
    Task<int> CreatePhaseAsync(Phase phase);
    Task<bool> UpdatePhaseAsync(Phase phase);

    Task<IReadOnlyList<Rater>> GetRatersAsync();
    Task<Rater?> GetRaterAsync(int id);
    Task<int> CreateRaterAsync(Rater rater);
    Task<bool> UpdateRaterAsync(Rater rater);

    Task<IReadOnlyList<ScoreCategory>> GetScoreCategoriesAsync();
    Task<ScoreCategory?> GetScoreCategoryAsync(int id);
    Task<int> CreateScoreCategoryAsync(ScoreCategory category);
    Task<bool> UpdateScoreCategoryAsync(ScoreCategory category);

    Task<IReadOnlyList<Status>> GetStatusesAsync();
    Task<Status?> GetStatusAsync(int id);
    Task<int> CreateStatusAsync(Status status);
    Task<bool> UpdateStatusAsync(Status status);
}
