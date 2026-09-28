using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Services;

/// <summary>
/// Business rules for the experiment design aggregate. The single invariant enforced everywhere
/// here is docs/ARCHITECTURE.md §5.3: once ExperimentVersion.LockedUTC is set, the ExperimentVersion
/// row itself and everything it governs (Iteration, Parameter, ScoreRule) is immutable through
/// ShepherdAPI — StatusID is the sole exception, so lifecycle state can still move to e.g. Retired.
/// </summary>
public class ExperimentDesignService(IExperimentDesignRepository repository) : IExperimentDesignService
{
    public Task<IReadOnlyList<ExperimentVersion>> GetExperimentVersionsAsync() => repository.GetExperimentVersionsAsync();

    public async Task<ExperimentVersion> GetExperimentVersionAsync(long id) =>
        await repository.GetExperimentVersionAsync(id) ?? throw NotFound("ExperimentVersion", id);

    public async Task<ExperimentVersion> CreateExperimentVersionAsync(CreateExperimentVersionRequest request)
    {
        var id = await repository.CreateExperimentVersionAsync(
            request.Name, request.VersionLabel, request.StatusID, request.EffectiveDateUTC, request.Description);
        return await GetExperimentVersionAsync(id);
    }

    public async Task<ExperimentVersion> UpdateExperimentVersionAsync(long id, UpdateExperimentVersionRequest request)
    {
        var current = await GetExperimentVersionAsync(id);
        EnsureUnlocked(current);

        await repository.UpdateExperimentVersionAsync(id, request.Name, request.VersionLabel, request.EffectiveDateUTC, request.Description);
        return await GetExperimentVersionAsync(id);
    }

    public async Task<ExperimentVersion> SetExperimentVersionStatusAsync(long id, SetExperimentVersionStatusRequest request)
    {
        // StatusID is explicitly exempt from the lock (docs/ARCHITECTURE.md §5.3) so lifecycle state
        // such as Retired can still be recorded after LockedUTC is set. No lock check here by design.
        await GetExperimentVersionAsync(id);
        await repository.SetExperimentVersionStatusAsync(id, request.StatusID);
        return await GetExperimentVersionAsync(id);
    }

    public async Task<ExperimentVersion> LockExperimentVersionAsync(long id, LockExperimentVersionRequest request)
    {
        var current = await GetExperimentVersionAsync(id);
        if (current.LockedUTC is not null)
        {
            throw new RecordLockedException(
                $"ExperimentVersion {id} is already locked as of {current.LockedUTC:O}. LockedUTC cannot be changed through normal ShepherdAPI operations.");
        }

        await repository.LockExperimentVersionAsync(id, request.StatusID, DateTime.UtcNow);
        return await GetExperimentVersionAsync(id);
    }

    // -- Iteration ------------------------------------------------------------

    public Task<IReadOnlyList<Iteration>> GetIterationsAsync(long experimentVersionId) => repository.GetIterationsByExperimentVersionAsync(experimentVersionId);

    public async Task<Iteration> GetIterationAsync(long id) =>
        await repository.GetIterationAsync(id) ?? throw NotFound("Iteration", id);

    public async Task<Iteration> CreateIterationAsync(CreateIterationRequest request)
    {
        var owner = await GetExperimentVersionAsync(request.ExperimentVersionID);
        EnsureUnlocked(owner);

        var iteration = new Iteration
        {
            ExperimentVersionID = request.ExperimentVersionID,
            Sequence = request.Sequence,
            IterationLabel = request.IterationLabel,
            Prompt = request.Prompt,
            Scenario = request.Scenario,
            Notes = request.Notes,
        };
        var id = await repository.CreateIterationAsync(iteration);
        return await GetIterationAsync(id);
    }

    public async Task<Iteration> UpdateIterationAsync(long id, UpdateIterationRequest request)
    {
        var iteration = await GetIterationAsync(id);
        var owner = await GetExperimentVersionAsync(iteration.ExperimentVersionID);
        EnsureUnlocked(owner);

        iteration.Sequence = request.Sequence;
        iteration.IterationLabel = request.IterationLabel;
        iteration.Prompt = request.Prompt;
        iteration.Scenario = request.Scenario;
        iteration.Notes = request.Notes;
        await repository.UpdateIterationAsync(iteration);
        return await GetIterationAsync(id);
    }

    public async Task DeleteIterationAsync(long id)
    {
        var iteration = await GetIterationAsync(id);
        var owner = await GetExperimentVersionAsync(iteration.ExperimentVersionID);
        EnsureUnlocked(owner);

        await repository.DeleteIterationAsync(id);
    }

    // -- Parameter --------------------------------------------------------------

    public Task<IReadOnlyList<Parameter>> GetParametersAsync(long experimentVersionId) => repository.GetParametersByExperimentVersionAsync(experimentVersionId);

    public async Task<Parameter> GetParameterAsync(long id) =>
        await repository.GetParameterAsync(id) ?? throw NotFound("Parameter", id);

    public async Task<Parameter> CreateParameterAsync(CreateParameterRequest request)
    {
        var owner = await GetExperimentVersionAsync(request.ExperimentVersionID);
        EnsureUnlocked(owner);

        var parameter = new Parameter
        {
            ExperimentVersionID = request.ExperimentVersionID,
            Name = request.Name,
            Value = request.Value,
            Unit = request.Unit,
            Notes = request.Notes,
        };
        var id = await repository.CreateParameterAsync(parameter);
        return await GetParameterAsync(id);
    }

    public async Task<Parameter> UpdateParameterAsync(long id, UpdateParameterRequest request)
    {
        var parameter = await GetParameterAsync(id);
        var owner = await GetExperimentVersionAsync(parameter.ExperimentVersionID);
        EnsureUnlocked(owner);

        parameter.Value = request.Value;
        parameter.Unit = request.Unit;
        parameter.Notes = request.Notes;
        await repository.UpdateParameterAsync(parameter);
        return await GetParameterAsync(id);
    }

    public async Task DeleteParameterAsync(long id)
    {
        var parameter = await GetParameterAsync(id);
        var owner = await GetExperimentVersionAsync(parameter.ExperimentVersionID);
        EnsureUnlocked(owner);

        await repository.DeleteParameterAsync(id);
    }

    // -- ScoreRule ----------------------------------------------------------------

    public Task<IReadOnlyList<ScoreRule>> GetScoreRulesAsync(long experimentVersionId) => repository.GetScoreRulesByExperimentVersionAsync(experimentVersionId);

    public async Task<ScoreRule> GetScoreRuleAsync(int id) =>
        await repository.GetScoreRuleAsync(id) ?? throw NotFound("ScoreRule", id);

    public async Task<ScoreRule> CreateScoreRuleAsync(CreateScoreRuleRequest request)
    {
        var owner = await GetExperimentVersionAsync(request.ExperimentVersionID);
        EnsureUnlocked(owner);
        EnsureScoreRange(request.MinScore, request.MaxScore);

        var scoreRule = new ScoreRule
        {
            ExperimentVersionID = request.ExperimentVersionID,
            PhaseID = request.PhaseID,
            ScoreCategoryID = request.ScoreCategoryID,
            WeightPercent = request.WeightPercent,
            MinScore = request.MinScore,
            MaxScore = request.MaxScore,
            Active = request.Active,
            RuleNotes = request.RuleNotes,
        };
        var id = await repository.CreateScoreRuleAsync(scoreRule);
        return await GetScoreRuleAsync(id);
    }

    /// <summary>Soft-deletes (RowState = 'D'). Idempotent: a no-op if the rule is already deleted, per docs/ARCHITECTURE.md and the add/delete-only lifecycle.</summary>
    public async Task DeleteScoreRuleAsync(int id)
    {
        var scoreRule = await GetScoreRuleAsync(id);
        var owner = await GetExperimentVersionAsync(scoreRule.ExperimentVersionID);
        EnsureUnlocked(owner);

        await repository.DeleteScoreRuleAsync(id);
    }

    // -- helpers ------------------------------------------------------------------

    private static void EnsureUnlocked(ExperimentVersion experimentVersion)
    {
        if (experimentVersion.LockedUTC is not null)
        {
            throw new RecordLockedException(
                $"ExperimentVersion {experimentVersion.ExperimentVersionID} was locked at {experimentVersion.LockedUTC:O} and its governed data is immutable through normal ShepherdAPI operations.");
        }
    }

    private static void EnsureScoreRange(int minScore, int maxScore)
    {
        if (minScore >= maxScore)
        {
            throw new ValidationAppException(nameof(maxScore), "MaxScore must be greater than MinScore.");
        }
    }

    private static NotFoundException NotFound(string entity, object id) => new($"{entity} {id} was not found.");
}
