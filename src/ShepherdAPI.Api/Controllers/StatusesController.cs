using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Controllers;

/// <summary>
/// Status is adjustable Alpha reference data (docs/ARCHITECTURE.md §5.3) — ShepherdAPI does not
/// hardcode which named values exist, beyond the ExperimentVersion lock action atomically setting
/// whichever StatusID the caller designates (see ExperimentVersionsController.Lock).
/// </summary>
[ApiController]
[Route("api/statuses")]
[Produces("application/json")]
public class StatusesController(ILookupRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Status>>> GetAll() => Ok(await repository.GetStatusesAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Status>> GetById(int id)
    {
        var status = await repository.GetStatusAsync(id) ?? throw new NotFoundException($"Status {id} was not found.");
        return Ok(status);
    }

    [HttpPost]
    public async Task<ActionResult<Status>> Create(StatusRequest request)
    {
        var status = new Status { Name = request.Name, Description = request.Description };
        var id = await repository.CreateStatusAsync(status);
        status.StatusID = id;
        return CreatedAtAction(nameof(GetById), new { id }, status);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Status>> Update(int id, StatusRequest request)
    {
        _ = await repository.GetStatusAsync(id) ?? throw new NotFoundException($"Status {id} was not found.");
        var status = new Status { StatusID = id, Name = request.Name, Description = request.Description };
        await repository.UpdateStatusAsync(status);
        return Ok(status);
    }
}
