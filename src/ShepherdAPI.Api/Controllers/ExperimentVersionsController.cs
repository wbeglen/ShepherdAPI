using Microsoft.AspNetCore.Mvc;
using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;

namespace ShepherdAPI.Api.Controllers;

[ApiController]
[Route("api/experiment-versions")]
[Produces("application/json")]
public class ExperimentVersionsController(IExperimentDesignService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExperimentVersion>>> GetAll() => Ok(await service.GetExperimentVersionsAsync());

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ExperimentVersion>> GetById(long id) => Ok(await service.GetExperimentVersionAsync(id));

    [HttpPost]
    public async Task<ActionResult<ExperimentVersion>> Create(CreateExperimentVersionRequest request)
    {
        var created = await service.CreateExperimentVersionAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.ExperimentVersionID }, created);
    }

    /// <summary>Updates descriptive fields only. Rejected with 409 once LockedUTC is set (docs/ARCHITECTURE.md §5.3).</summary>
    [HttpPut("{id:long}")]
    public async Task<ActionResult<ExperimentVersion>> Update(long id, UpdateExperimentVersionRequest request) =>
        Ok(await service.UpdateExperimentVersionAsync(id, request));

    /// <summary>
    /// StatusID is exempt from the lock, so lifecycle transitions such as Retired remain possible
    /// after LockedUTC is set (docs/ARCHITECTURE.md §5.3). Not for establishing the lock itself — use Lock.
    /// </summary>
    [HttpPut("{id:long}/status")]
    public async Task<ActionResult<ExperimentVersion>> SetStatus(long id, SetExperimentVersionStatusRequest request) =>
        Ok(await service.SetExperimentVersionStatusAsync(id, request));

    /// <summary>
    /// The single, irreversible action that atomically sets StatusID and LockedUTC together,
    /// declaring the experimental record final (docs/ARCHITECTURE.md §5.3). Returns 409 if already locked.
    /// </summary>
    [HttpPost("{id:long}/lock")]
    public async Task<ActionResult<ExperimentVersion>> Lock(long id, LockExperimentVersionRequest request) =>
        Ok(await service.LockExperimentVersionAsync(id, request));
}
