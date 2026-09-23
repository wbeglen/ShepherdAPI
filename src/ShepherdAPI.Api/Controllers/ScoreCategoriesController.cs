using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/score-categories")]
[Produces("application/json")]
public class ScoreCategoriesController(ILookupRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScoreCategory>>> GetAll() => Ok(await repository.GetScoreCategoriesAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ScoreCategory>> GetById(int id)
    {
        var category = await repository.GetScoreCategoryAsync(id) ?? throw new NotFoundException($"ScoreCategory {id} was not found.");
        return Ok(category);
    }

    [HttpPost]
    public async Task<ActionResult<ScoreCategory>> Create(ScoreCategoryRequest request)
    {
        var category = new ScoreCategory { Name = request.Name, Description = request.Description, HigherIsBetter = request.HigherIsBetter };
        var id = await repository.CreateScoreCategoryAsync(category);
        category.ScoreCategoryID = id;
        return CreatedAtAction(nameof(GetById), new { id }, category);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ScoreCategory>> Update(int id, ScoreCategoryRequest request)
    {
        _ = await repository.GetScoreCategoryAsync(id) ?? throw new NotFoundException($"ScoreCategory {id} was not found.");
        var category = new ScoreCategory { ScoreCategoryID = id, Name = request.Name, Description = request.Description, HigherIsBetter = request.HigherIsBetter };
        await repository.UpdateScoreCategoryAsync(category);
        return Ok(category);
    }
}
