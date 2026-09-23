using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/phases")]
[Produces("application/json")]
public class PhasesController(ILookupRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Phase>>> GetAll() => Ok(await repository.GetPhasesAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Phase>> GetById(int id)
    {
        var phase = await repository.GetPhaseAsync(id) ?? throw new NotFoundException($"Phase {id} was not found.");
        return Ok(phase);
    }

    [HttpPost]
    public async Task<ActionResult<Phase>> Create(PhaseRequest request)
    {
        var phase = new Phase { Name = request.Name, Sequence = request.Sequence, Description = request.Description };
        var id = await repository.CreatePhaseAsync(phase);
        phase.PhaseID = id;
        return CreatedAtAction(nameof(GetById), new { id }, phase);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Phase>> Update(int id, PhaseRequest request)
    {
        _ = await repository.GetPhaseAsync(id) ?? throw new NotFoundException($"Phase {id} was not found.");
        var phase = new Phase { PhaseID = id, Name = request.Name, Sequence = request.Sequence, Description = request.Description };
        await repository.UpdatePhaseAsync(phase);
        return Ok(phase);
    }
}
