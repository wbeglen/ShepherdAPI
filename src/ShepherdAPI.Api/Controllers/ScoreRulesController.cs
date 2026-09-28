using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

/// <summary>
/// ScoreRule has an add/delete-only lifecycle — definitions are immutable after creation, so
/// there is deliberately no PUT/PATCH endpoint. GetAll returns only Active (RowState = 'A')
/// rules; GetById resolves regardless of RowState so historical ScoreObservation references
/// to a retired rule keep working.
/// </summary>
[ApiController]
[Route("api/score-rules")]
[Produces("application/json")]
public class ScoreRulesController(IExperimentDesignService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScoreRule>>> GetAll([FromQuery] long experimentVersionId) =>
        Ok(await service.GetScoreRulesAsync(experimentVersionId));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ScoreRule>> GetById(int id) => Ok(await service.GetScoreRuleAsync(id));

    [HttpPost]
    public async Task<ActionResult<ScoreRule>> Create(CreateScoreRuleRequest request)
    {
        var created = await service.CreateScoreRuleAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.ScoreRuleID }, created);
    }

    /// <summary>Retires the rule (RowState = 'D'). Does not physically delete it — existing historical references remain valid.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await service.DeleteScoreRuleAsync(id);
        return NoContent();
    }
}
