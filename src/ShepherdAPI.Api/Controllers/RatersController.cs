using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/raters")]
[Produces("application/json")]
public class RatersController(ILookupRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Rater>>> GetAll() => Ok(await repository.GetRatersAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Rater>> GetById(int id)
    {
        var rater = await repository.GetRaterAsync(id) ?? throw new NotFoundException($"Rater {id} was not found.");
        return Ok(rater);
    }

    [HttpPost]
    public async Task<ActionResult<Rater>> Create(RaterRequest request)
    {
        var rater = new Rater { Name = request.Name, RaterType = request.RaterType, AgentID = request.AgentID };
        var id = await repository.CreateRaterAsync(rater);
        rater.RaterID = id;
        return CreatedAtAction(nameof(GetById), new { id }, rater);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Rater>> Update(int id, RaterRequest request)
    {
        _ = await repository.GetRaterAsync(id) ?? throw new NotFoundException($"Rater {id} was not found.");
        var rater = new Rater { RaterID = id, Name = request.Name, RaterType = request.RaterType, AgentID = request.AgentID };
        await repository.UpdateRaterAsync(rater);
        return Ok(rater);
    }
}
