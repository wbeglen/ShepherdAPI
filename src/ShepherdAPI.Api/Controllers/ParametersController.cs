using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/parameters")]
[Produces("application/json")]
public class ParametersController(IExperimentDesignService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Parameter>>> GetAll([FromQuery] long experimentVersionId) =>
        Ok(await service.GetParametersAsync(experimentVersionId));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<Parameter>> GetById(long id) => Ok(await service.GetParameterAsync(id));

    [HttpPost]
    public async Task<ActionResult<Parameter>> Create(CreateParameterRequest request)
    {
        var created = await service.CreateParameterAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.ParameterID }, created);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<Parameter>> Update(long id, UpdateParameterRequest request) =>
        Ok(await service.UpdateParameterAsync(id, request));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        await service.DeleteParameterAsync(id);
        return NoContent();
    }
}
