using Microsoft.Data.SqlClient;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Services;

/// <summary>
/// Business rules for the experiment design aggregate. Design writes (ExperimentVersion fields and
/// Iteration, Parameter and ScoreRule create/update/delete) are performed by controlled-write procedures
/// that take the parent-version lock and reject the write when the version is locked (version-locked) or
/// already has an Execution (design-frozen, Issue #4). This service only resolves 404s and shapes the rows.
/// StatusID is exempt from both rules, so lifecycle state can still move to e.g. Retired.
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
        if (!await repository.UpdateExperimentVersionAsync(id, request.Name, request.VersionLabel, request.EffectiveDateUTC, request.Description))
        {
            throw NotFound("ExperimentVersion", id);
        }

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
        // request.StatusID is accepted for compatibility and deliberately ignored (Issue #2).
        try
        {
            if (await repository.FinalizeExperimentVersionAsync(id) is null)
            {
                throw NotFound("ExperimentVersion", id);
            }
        }
        catch (SqlException ex) when (FinalizationErrors.TryTranslate(ex, out var translated))
        {
            throw translated;
        }

        return await GetExperimentVersionAsync(id);
    }

    // -- Iteration ------------------------------------------------------------

    public Task<IReadOnlyList<Iteration>> GetIterationsAsync(long experimentVersionId) => repository.GetIterationsByExperimentVersionAsync(experimentVersionId);

    public async Task<Iteration> GetIterationAsync(long id) =>
        await repository.GetIterationAsync(id) ?? throw NotFound("Iteration", id);

    public async Task<Iteration> CreateIterationAsync(CreateIterationRequest request)
    {
        var iteration = new Iteration
        {
            ExperimentVersionID = request.ExperimentVersionID,
            Sequence = request.Sequence,
            IterationLabel = request.IterationLabel,
            Prompt = request.Prompt,
            Scenario = request.Scenario,
            Notes = request.Notes,
        };
        var id = await repository.CreateIterationAsync(iteration) ?? throw NotFound("ExperimentVersion", request.ExperimentVersionID);
        return await GetIterationAsync(id);
    }

    public async Task<Iteration> UpdateIterationAsync(long id, UpdateIterationRequest request)
    {
        var iteration = await GetIterationAsync(id);

        iteration.Sequence = request.Sequence;
        iteration.IterationLabel = request.IterationLabel;
        iteration.Prompt = request.Prompt;
        iteration.Scenario = request.Scenario;
        iteration.Notes = request.Notes;
        if (!await repository.UpdateIterationAsync(iteration))
        {
            throw NotFound("Iteration", id);
        }

        return await GetIterationAsync(id);
    }

    public async Task DeleteIterationAsync(long id)
    {
        if (!await repository.DeleteIterationAsync(id))
        {
            throw NotFound("Iteration", id);
        }
    }

    // -- Parameter --------------------------------------------------------------

    public Task<IReadOnlyList<Parameter>> GetParametersAsync(long experimentVersionId) => repository.GetParametersByExperimentVersionAsync(experimentVersionId);

    public async Task<Parameter> GetParameterAsync(long id) =>
        await repository.GetParameterAsync(id) ?? throw NotFound("Parameter", id);

    public async Task<Parameter> CreateParameterAsync(CreateParameterRequest request)
    {
        var parameter = new Parameter
        {
            ExperimentVersionID = request.ExperimentVersionID,
            Name = request.Name,
            Value = request.Value,
            Unit = request.Unit,
            Notes = request.Notes,
        };
        var id = await repository.CreateParameterAsync(parameter) ?? throw NotFound("ExperimentVersion", request.ExperimentVersionID);
        return await GetParameterAsync(id);
    }

    public async Task<Parameter> UpdateParameterAsync(long id, UpdateParameterRequest request)
    {
        var parameter = await GetParameterAsync(id);

        parameter.Value = request.Value;
        parameter.Unit = request.Unit;
        parameter.Notes = request.Notes;
        if (!await repository.UpdateParameterAsync(parameter))
        {
            throw NotFound("Parameter", id);
        }

        return await GetParameterAsync(id);
    }

    public async Task DeleteParameterAsync(long id)
    {
        if (!await repository.DeleteParameterAsync(id))
        {
            throw NotFound("Parameter", id);
        }
    }

    // -- ScoreRule ----------------------------------------------------------------

    public Task<IReadOnlyList<ScoreRule>> GetScoreRulesAsync(long experimentVersionId) => repository.GetScoreRulesByExperimentVersionAsync(experimentVersionId);

    public async Task<ScoreRule> GetScoreRuleAsync(int id) =>
        await repository.GetScoreRuleAsync(id) ?? throw NotFound("ScoreRule", id);

    public async Task<ScoreRule> CreateScoreRuleAsync(CreateScoreRuleRequest request)
    {
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
        var id = await repository.CreateScoreRuleAsync(scoreRule) ?? throw NotFound("ExperimentVersion", request.ExperimentVersionID);
        return await GetScoreRuleAsync(id);
    }

    /// <summary>
    /// The dedicated ScoreRule soft-delete (Issue #4): RowState 'A' becomes 'D' (HTTP 204). Not idempotent: an
    /// already-deleted rule is the score-rule-already-deleted error, and any other RowState is
    /// score-rule-invalid-rowstate. A live version (it has Executions and is not locked) is design-frozen. The
    /// lifecycle check runs before the RowState check, both under the parent-version lock, inside the procedure.
    /// </summary>
    public async Task DeleteScoreRuleAsync(int id)
    {
        if (!await repository.SoftDeleteScoreRuleAsync(id))
        {
            throw NotFound("ScoreRule", id);
        }
    }

    // -- helpers ------------------------------------------------------------------

    private static void EnsureScoreRange(int minScore, int maxScore)
    {
        if (minScore >= maxScore)
        {
            throw new ValidationAppException(nameof(maxScore), "MaxScore must be greater than MinScore.");
        }
    }

    private static NotFoundException NotFound(string entity, object id) => new($"{entity} {id} was not found.");
}
