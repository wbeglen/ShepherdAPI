using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/score-observations")]
[Produces("application/json")]
public class ScoreObservationsController(IRecordingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScoreObservation>>> GetAll([FromQuery] long executionId) =>
        Ok(await service.GetScoreObservationsAsync(executionId));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ScoreObservation>> GetById(long id) => Ok(await service.GetScoreObservationAsync(id));

    [HttpPost]
    public async Task<ActionResult<ScoreObservation>> Create(CreateScoreObservationRequest request)
    {
        var created = await service.CreateScoreObservationAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.ScoreObservationID }, created);
    }

    /// <summary>The correction path for scoring/notes content while the governing ExperimentVersion is unlocked.</summary>
    [HttpPut("{id:long}/content")]
    public async Task<ActionResult<ScoreObservation>> UpdateContent(long id, UpdateScoreObservationContentRequest request) =>
        Ok(await service.UpdateScoreObservationContentAsync(id, request));

    [HttpGet("{id:long}/evidence")]
    public async Task<ActionResult<IReadOnlyList<ScoreObservationEvidence>>> GetEvidence(long id) =>
        Ok(await service.GetEvidenceAsync(id));

    [HttpPost("{id:long}/evidence")]
    public async Task<ActionResult<ScoreObservationEvidence>> AddEvidence(long id, CreateScoreObservationEvidenceRequest request)
    {
        var link = await service.AddEvidenceAsync(id, request);
        return CreatedAtAction(nameof(GetEvidence), new { id }, link);
    }

    [HttpDelete("{id:long}/evidence/{artifactId:long}")]
    public async Task<IActionResult> RemoveEvidence(long id, long artifactId)
    {
        await service.RemoveEvidenceAsync(id, artifactId);
        return NoContent();
    }
}
