using FdeTracker.Api.Filters;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>Daily status as a weekly timesheet. `week` is the Monday (YYYY-MM-DD).</summary>
[Route("api/programs/{id:guid}/timesheet"), RequireApproved]
public sealed class TimesheetController(TimesheetService timesheets) : ApiControllerBase
{
    [HttpGet("{week}")]
    public async Task<IActionResult> Get(Guid id, string week, [FromQuery(Name = "user_id")] Guid? userId) => Ok(await timesheets.Get(CurrentUser, id, week, userId));

    [HttpPut("{week}")]
    public async Task<IActionResult> Save(Guid id, string week, TimesheetRequest body) => Ok(await timesheets.Save(CurrentUser, id, week, body));
}
