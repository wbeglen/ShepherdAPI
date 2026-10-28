using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

/// <summary>
/// ScoreRules are created, updated and physically deleted only while their version is unlocked and has no
/// Execution (Issue #7); otherwise each write is 409 version-locked or design-frozen. Once an Execution exists
/// the rules are fixed for good, so every rule a ScoreObservation or snapshot refers to still exists.
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

    /// <summary>Replaces WeightPercent, MinScore, MaxScore, Active and RuleNotes. The version, phase and category never change.</summary>
    [ProducesResponseType(typeof(ScoreRule), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ScoreRule>> Update(int id, UpdateScoreRuleRequest request) =>
        Ok(await service.UpdateScoreRuleAsync(id, request));

    /// <summary>
    /// Physically deletes the rule (204), freeing its version/phase/category combination for a new rule. A deleted
    /// rule no longer exists, so deleting it again is 404.
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
