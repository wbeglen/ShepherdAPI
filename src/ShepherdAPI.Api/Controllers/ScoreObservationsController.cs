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

    /// <summary>
    /// Records an observation. 404 if the Execution does not exist. Refused with <c>code</c> =
    /// <c>version-locked</c> (409), <c>version-phase-mismatch</c> (409: the rule belongs to another version or phase),
    /// <c>out-of-range-score</c> (400: outside the rule's
    /// MinScore..MaxScore, inclusive) or <c>duplicate-observation</c> (409: same Execution, rule and rater).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ScoreObservation), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScoreObservation>> Create(CreateScoreObservationRequest request)
    {
        var created = await service.CreateScoreObservationAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.ScoreObservationID }, created);
    }

    /// <summary>
    /// The correction path for scoring/notes content while the governing ExperimentVersion is unlocked. 404 if the
    /// observation does not exist; 409 <c>version-locked</c> once the version is locked; 400 <c>out-of-range-score</c>
    /// if the score is outside the rule's MinScore..MaxScore (inclusive).
    /// </summary>
    [HttpPut("{id:long}/content")]
    [ProducesResponseType(typeof(ScoreObservation), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
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
