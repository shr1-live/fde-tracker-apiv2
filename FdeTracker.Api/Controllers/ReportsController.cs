using FdeTracker.Api.Filters;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>Progress reports. Everyone approved reads their own; reviewers and the admin read anyone's (members get 403).</summary>
[Route("api/reports"), RequireApproved]
public sealed class ReportsController(ReportService reports) : ApiControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me() => Ok(await reports.For(CurrentUser, CurrentUser.Id));

    [HttpGet("people"), RequireRole("reviewer", "admin")]
    public async Task<IActionResult> People([FromQuery] Guid? batch) => Ok(await reports.People(batch));

    [HttpGet("people/{id:guid}"), RequireRole("reviewer", "admin")]
    public async Task<IActionResult> Person(Guid id) => Ok(await reports.For(CurrentUser, id));
}
