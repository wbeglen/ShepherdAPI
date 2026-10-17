using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/iterations")]
[Produces("application/json")]
public class IterationsController(IExperimentDesignService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Iteration>>> GetAll([FromQuery] long experimentVersionId) =>
        Ok(await service.GetIterationsAsync(experimentVersionId));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<Iteration>> GetById(long id) => Ok(await service.GetIterationAsync(id));

    [ProducesResponseType(typeof(Iteration), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [HttpPost]
    public async Task<ActionResult<Iteration>> Create(CreateIterationRequest request)
    {
        var created = await service.CreateIterationAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.IterationID }, created);
    }

    [ProducesResponseType(typeof(Iteration), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<Iteration>> Update(long id, UpdateIterationRequest request) =>
        Ok(await service.UpdateIterationAsync(id, request));

    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await service.DeleteIterationAsync(id);
        return NoContent();
    }
}
