using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

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

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ScoreRule>> Update(int id, UpdateScoreRuleRequest request) =>
        Ok(await service.UpdateScoreRuleAsync(id, request));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await service.DeleteScoreRuleAsync(id);
        return NoContent();
    }
}
