using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

/// <summary>
/// Evidence metadata registration (docs/ARCHITECTURE.md §4.4). Artifact rows are insert-only —
/// no update or delete endpoint is exposed, ever. SHA-256 is the evidence identity mechanism.
/// </summary>
[ApiController]
[Route("api/artifacts")]
[Produces("application/json")]
public class ArtifactsController(IRecordingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Artifact>>> GetAll([FromQuery] long executionId) =>
        Ok(await service.GetArtifactsAsync(executionId));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<Artifact>> GetById(long id) => Ok(await service.GetArtifactAsync(id));

    [HttpPost]
    public async Task<ActionResult<Artifact>> Create(CreateArtifactRequest request)
    {
        var created = await service.CreateArtifactAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.ArtifactID }, created);
    }
}
