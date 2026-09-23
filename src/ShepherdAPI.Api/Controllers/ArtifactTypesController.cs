using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Models;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/artifact-types")]
[Produces("application/json")]
public class ArtifactTypesController(ILookupRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArtifactType>>> GetAll() => Ok(await repository.GetArtifactTypesAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ArtifactType>> GetById(int id)
    {
        var artifactType = await repository.GetArtifactTypeAsync(id) ?? throw new NotFoundException($"ArtifactType {id} was not found.");
        return Ok(artifactType);
    }

    [HttpPost]
    public async Task<ActionResult<ArtifactType>> Create(ArtifactTypeRequest request)
    {
        var artifactType = new ArtifactType { Name = request.Name, Description = request.Description };
        var id = await repository.CreateArtifactTypeAsync(artifactType);
        artifactType.ArtifactTypeID = id;
        return CreatedAtAction(nameof(GetById), new { id }, artifactType);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ArtifactType>> Update(int id, ArtifactTypeRequest request)
    {
        _ = await repository.GetArtifactTypeAsync(id) ?? throw new NotFoundException($"ArtifactType {id} was not found.");
        var artifactType = new ArtifactType { ArtifactTypeID = id, Name = request.Name, Description = request.Description };
        await repository.UpdateArtifactTypeAsync(artifactType);
        return Ok(artifactType);
    }
}
