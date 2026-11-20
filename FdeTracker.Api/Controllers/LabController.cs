using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services.Lab;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>
/// The practice lab's model runs. They sit under /api/assistant so a request may carry the person's own Gemini key
/// (X-Gemini-Key and X-Gemini-Model); without it the server's model is used. Same daily limit as the assistant.
/// </summary>
[Route("api/assistant/lab"), RequireApproved]
public sealed class LabController(LabService lab) : ApiControllerBase
{
    [HttpGet("day1/cases")]
    public IActionResult Day1Cases() => Ok(lab.Day1Cases());

    [HttpPost("day1/{caseId}")]
    public async Task<IActionResult> Day1(string caseId, CancellationToken ct) => Ok(await lab.RunDay1(CurrentUser.Id, caseId, ct));

    /// <summary>One batch of the Day 2 candidates (4 per call). The answer says how many batches there are, so the client can loop.</summary>
    [HttpPost("day2/{version}")]
    public async Task<IActionResult> Day2(string version, [FromQuery] int batch = 0, CancellationToken ct = default) => Ok(await lab.RunDay2(CurrentUser.Id, version, batch, ct));
}

/// <summary>Saved lab runs and the shared samples. Not under /api/assistant, so a Gemini key header is never even read here.</summary>
[Route("api/lab"), RequireApproved]
public sealed class LabRunsController(LabRunService runs) : ApiControllerBase
{
    [HttpPost("runs")]
    public async Task<IActionResult> Save(SaveLabRunRequest body) => StatusCode(201, await runs.Save(CurrentUser.Id, body));

    [HttpGet("runs/mine")]
    public async Task<IActionResult> Mine() => Ok(await runs.Mine(CurrentUser.Id));

    [HttpDelete("runs/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await runs.Delete(CurrentUser, id);
        return NoContent();
    }

    [HttpGet("samples")]
    public async Task<IActionResult> Samples() => Ok(await runs.Samples());

    [HttpPost("runs/{id:guid}/pin"), RequireRole("admin")]
    public async Task<IActionResult> Pin(Guid id) => Ok(await runs.Pin(CurrentUser, id));

    [HttpDelete("runs/{id:guid}/pin"), RequireRole("admin")]
    public async Task<IActionResult> Unpin(Guid id) => Ok(await runs.Unpin(id));
}
