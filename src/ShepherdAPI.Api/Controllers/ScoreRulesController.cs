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

    [ProducesResponseType(typeof(ScoreRule), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [HttpPost]
    public async Task<ActionResult<ScoreRule>> Create(CreateScoreRuleRequest request)
    {
        var created = await service.CreateScoreRuleAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.ScoreRuleID }, created);
    }

    /// <summary>
    /// The dedicated ScoreRule soft-delete: changes only RowState ('A' to 'D') and returns 204. The row is never
    /// physically deleted, so historical references stay valid. Not idempotent: an already-deleted rule is 409
    /// score-rule-already-deleted and any other RowState is 409 score-rule-invalid-rowstate. A live version (it has
    /// Executions and is not locked) is 409 design-frozen; this is checked before RowState. A version with no
    /// Executions, or a locked one, permits the delete.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id)
    {
        await service.DeleteScoreRuleAsync(id);
        return NoContent();
    }
}
