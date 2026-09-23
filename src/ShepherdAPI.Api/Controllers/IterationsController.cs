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

    [HttpPost]
    public async Task<ActionResult<Iteration>> Create(CreateIterationRequest request)
    {
        var created = await service.CreateIterationAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.IterationID }, created);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<Iteration>> Update(long id, UpdateIterationRequest request) =>
        Ok(await service.UpdateIterationAsync(id, request));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await service.DeleteIterationAsync(id);
        return NoContent();
    }
}
