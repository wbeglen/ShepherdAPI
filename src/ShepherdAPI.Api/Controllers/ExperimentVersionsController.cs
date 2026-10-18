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

    /// <summary>
    /// Updates descriptive fields only (Name, VersionLabel, EffectiveDateUTC, Description). Rejected with 409
    /// version-locked once LockedUTC is set, and with 409 design-frozen once the version has any Execution.
    /// </summary>
    [ProducesResponseType(typeof(ExperimentVersion), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
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
    /// Finalizes the version (Issue #2): in one transaction it snapshots every ScoreObservation into
    /// ScoreObservationSnapshot and sets LockedUTC. Irreversible. The optional StatusID in the request is ignored.
    /// Returns 409 version-locked if already locked, or a 409 naming the failed completeness or integrity check
    /// (and the offending Executions, phases or observations) if the version is not ready.
    /// </summary>
    [HttpPost("{id:long}/lock")]
    [ProducesResponseType(typeof(ExperimentVersion), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExperimentVersion>> Lock(long id, LockExperimentVersionRequest request) =>
        Ok(await service.LockExperimentVersionAsync(id, request));
}
