using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/agents")]
[Produces("application/json")]
public class AgentsController(ILookupRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Agent>>> GetAll() => Ok(await repository.GetAgentsAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Agent>> GetById(int id)
    {
        var agent = await repository.GetAgentAsync(id) ?? throw new NotFoundException($"Agent {id} was not found.");
        return Ok(agent);
    }

    [HttpPost]
    public async Task<ActionResult<Agent>> Create(AgentRequest request)
    {
        var agent = new Agent
        {
            Name = request.Name,
            Provider = request.Provider,
            DefaultModel = request.DefaultModel,
            DefaultInterface = request.DefaultInterface,
            Active = request.Active,
        };
        var id = await repository.CreateAgentAsync(agent);
        agent.AgentID = id;
        return CreatedAtAction(nameof(GetById), new { id }, agent);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Agent>> Update(int id, AgentRequest request)
    {
        _ = await repository.GetAgentAsync(id) ?? throw new NotFoundException($"Agent {id} was not found.");
        var agent = new Agent
        {
            AgentID = id,
            Name = request.Name,
            Provider = request.Provider,
            DefaultModel = request.DefaultModel,
            DefaultInterface = request.DefaultInterface,
            Active = request.Active,
        };
        await repository.UpdateAgentAsync(agent);
        return Ok(agent);
    }
}
