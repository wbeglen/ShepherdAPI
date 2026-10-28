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
        var unknownEnds = ValidateUnknownEnds(request.UnknownEnds);
        try
        {
            if (await repository.FinalizeExperimentVersionAsync(id, unknownEnds) is null)
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

    public async Task<IReadOnlyList<FinalizationException>> GetFinalizationExceptionsAsync(long experimentVersionId)
    {
        await GetExperimentVersionAsync(experimentVersionId);
        return await repository.GetFinalizationExceptionsAsync(experimentVersionId);
    }

    /// <summary>
    /// The request's own shape (V11): positive, distinct ExecutionIDs and explanations that are not blank and fit. Whether
    /// each Execution belongs to the version and has no EndUTC is decided by the procedure, under the lock (50510).
    /// </summary>
    private static IReadOnlyList<(long ExecutionID, string Explanation)> ValidateUnknownEnds(List<UnknownEndAcknowledgment>? unknownEnds)
    {
        if (unknownEnds is null || unknownEnds.Count == 0)
        {
            return [];
        }

        const string field = nameof(LockExperimentVersionRequest.UnknownEnds);
        if (unknownEnds.Any(u => u is null || u.ExecutionID <= 0))
        {
            throw new ValidationAppException(field, "Every unknown end must name an ExecutionID greater than zero.");
        }

        if (unknownEnds.Any(u => string.IsNullOrWhiteSpace(u.Explanation)))
        {
            throw new ValidationAppException(field, "Every unknown end must have an explanation that is not blank.");
        }

        if (unknownEnds.Any(u => u.Explanation.Length > UnknownEndAcknowledgment.ExplanationMaxLength))
        {
            throw new ValidationAppException(field, $"An explanation can be at most {UnknownEndAcknowledgment.ExplanationMaxLength} characters.");
        }

        var duplicates = unknownEnds.GroupBy(u => u.ExecutionID).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new ValidationAppException(field, $"Each Execution can be named only once; repeated: {string.Join(", ", duplicates)}.");
        }

        return unknownEnds.Select(u => (u.ExecutionID, u.Explanation)).ToList();
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

    public async Task<ScoreRule> UpdateScoreRuleAsync(int id, UpdateScoreRuleRequest request)
    {
        EnsureScoreRange(request.MinScore, request.MaxScore);

        var scoreRule = await GetScoreRuleAsync(id);

        scoreRule.WeightPercent = request.WeightPercent;
        scoreRule.MinScore = request.MinScore;
        scoreRule.MaxScore = request.MaxScore;
        scoreRule.Active = request.Active;
        scoreRule.RuleNotes = request.RuleNotes;
        if (!await repository.UpdateScoreRuleAsync(scoreRule))
        {
            throw NotFound("ScoreRule", id);
        }

        return await GetScoreRuleAsync(id);
    }

    /// <summary>A physical delete (Issue #7); a second delete of the same rule is therefore 404.</summary>
    public async Task DeleteScoreRuleAsync(int id)
    {
        if (!await repository.DeleteScoreRuleAsync(id))
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
