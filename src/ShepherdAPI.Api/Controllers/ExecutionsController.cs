using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/executions")]
[Produces("application/json")]
public class ExecutionsController(IRecordingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Execution>>> GetAll([FromQuery] long iterationId) =>
        Ok(await service.GetExecutionsAsync(iterationId));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<Execution>> GetById(long id) => Ok(await service.GetExecutionAsync(id));

    [HttpPost]
    public async Task<ActionResult<Execution>> Create(CreateExecutionRequest request)
    {
        var created = await service.CreateExecutionAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.ExecutionID }, created);
    }

    /// <summary>
    /// Replaces the run-facts that remain mutable until EndUTC is set. Returns 409 once EndUTC is
    /// already set, or once the governing ExperimentVersion is locked (docs/ARCHITECTURE.md §5).
    /// </summary>
    [HttpPut("{id:long}/run-facts")]
    public async Task<ActionResult<Execution>> UpdateRunFacts(long id, UpdateExecutionRunFactsRequest request) =>
        Ok(await service.UpdateExecutionRunFactsAsync(id, request));
}
