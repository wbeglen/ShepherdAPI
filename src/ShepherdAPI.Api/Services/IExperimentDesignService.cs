using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Services;

public interface IExperimentDesignService
{
    Task<IReadOnlyList<ExperimentVersion>> GetExperimentVersionsAsync();
    Task<ExperimentVersion> GetExperimentVersionAsync(long id);
    Task<ExperimentVersion> CreateExperimentVersionAsync(CreateExperimentVersionRequest request);
    Task<ExperimentVersion> UpdateExperimentVersionAsync(long id, UpdateExperimentVersionRequest request);
    Task<ExperimentVersion> SetExperimentVersionStatusAsync(long id, SetExperimentVersionStatusRequest request);
    Task<ExperimentVersion> LockExperimentVersionAsync(long id, LockExperimentVersionRequest request);

    Task<IReadOnlyList<Iteration>> GetIterationsAsync(long experimentVersionId);
    Task<Iteration> GetIterationAsync(long id);
    Task<Iteration> CreateIterationAsync(CreateIterationRequest request);
    Task<Iteration> UpdateIterationAsync(long id, UpdateIterationRequest request);
    Task DeleteIterationAsync(long id);

    Task<IReadOnlyList<Parameter>> GetParametersAsync(long experimentVersionId);
    Task<Parameter> GetParameterAsync(long id);
    Task<Parameter> CreateParameterAsync(CreateParameterRequest request);
    Task<Parameter> UpdateParameterAsync(long id, UpdateParameterRequest request);
    Task DeleteParameterAsync(long id);

    // ScoreRule — add/delete-only lifecycle; no update (definitions are immutable after creation).
    Task<IReadOnlyList<ScoreRule>> GetScoreRulesAsync(long experimentVersionId);
    Task<ScoreRule> GetScoreRuleAsync(int id);
    Task<ScoreRule> CreateScoreRuleAsync(CreateScoreRuleRequest request);
    Task DeleteScoreRuleAsync(int id);
}
