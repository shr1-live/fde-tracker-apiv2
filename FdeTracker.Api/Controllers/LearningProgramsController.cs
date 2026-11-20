using System.Globalization;
using FdeTracker.Api.Common;
using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>Programs V3 (docs/PROGRAMS-V3.md). The services check the person's role in each program: outside it 404, not allowed 403.</summary>
[Route("api/programs"), RequireApproved]
public sealed class LearningProgramsController(LearningProgramService programs, ProgramStatusService status) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await programs.List(CurrentUser));

    [HttpPost]
    public async Task<IActionResult> Create(CreateLpProgramRequest body) => StatusCode(201, await programs.Create(CurrentUser, body));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id) => Ok(await programs.Detail(CurrentUser, id));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, PatchLpProgramRequest body) => Ok(await programs.Update(CurrentUser, id, body));

    // ---- plan

    [HttpPost("{id:guid}/days")]
    public async Task<IActionResult> AddDay(Guid id, CreateLpDayRequest body) => StatusCode(201, await programs.AddDay(CurrentUser, id, body));

    [HttpPatch("{id:guid}/days/{dayId:guid}")]
    public async Task<IActionResult> UpdateDay(Guid id, Guid dayId, PatchLpDayRequest body) => Ok(await programs.UpdateDay(CurrentUser, id, dayId, body));

    [HttpDelete("{id:guid}/days/{dayId:guid}")]
    public async Task<IActionResult> DeleteDay(Guid id, Guid dayId)
    {
        await programs.DeleteDay(CurrentUser, id, dayId);
        return NoContent();
    }

    [HttpPut("{id:guid}/forms/{form}")]
    public async Task<IActionResult> SaveForm(Guid id, string form, SaveLpFormRequest body) => Ok(await programs.SaveForm(CurrentUser, id, form, body));

    // ---- people

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> Members(Guid id) => Ok(await programs.Members(CurrentUser, id));

    [HttpPost("{id:guid}/invites")]
    public async Task<IActionResult> Invite(Guid id, LpInviteRequest body) => Ok(await programs.Invite(CurrentUser, id, body));

    [HttpDelete("{id:guid}/invites/{inviteId:guid}")]
    public async Task<IActionResult> CancelInvite(Guid id, Guid inviteId)
    {
        await programs.CancelInvite(CurrentUser, id, inviteId);
        return NoContent();
    }

    [HttpPatch("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> SetRole(Guid id, Guid userId, LpRoleRequest body) => Ok(await programs.SetRole(CurrentUser, id, userId, body.Role));

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
    {
        await programs.RemoveMember(CurrentUser, id, userId);
        return NoContent();
    }

    // ---- status

    [HttpGet("{id:guid}/daily")]
    public async Task<IActionResult> Daily(Guid id, [FromQuery(Name = "user_id")] Guid? userId, [FromQuery] string? from, [FromQuery] string? to) =>
        Ok(await status.Daily(CurrentUser, id, userId, from, to));

    [HttpPut("{id:guid}/daily/{date}")]
    public async Task<IActionResult> SaveDaily(Guid id, string date, LpDailyRequest body) => Ok(await status.SaveDaily(CurrentUser, id, date, body.Values));

    [HttpGet("{id:guid}/day-status")]
    public async Task<IActionResult> DayStatus(Guid id, [FromQuery(Name = "user_id")] Guid? userId) => Ok(await status.DayStatus(CurrentUser, id, userId));

    [HttpPut("{id:guid}/day-status/{dayId:guid}")]
    public async Task<IActionResult> SaveDayStatus(Guid id, Guid dayId, LpDayStatusRequest body) =>
        Ok(await status.SaveDayStatus(CurrentUser, id, dayId, body.Values, body.Completed));

    // ---- reports

    [HttpGet("{id:guid}/reports/daily")]
    public async Task<IActionResult> DailyReport(Guid id, [FromQuery] string? from, [FromQuery] string? to) => Ok(await status.DailyReport(CurrentUser, id, from, to));

    [HttpGet("{id:guid}/reports/program")]
    public async Task<IActionResult> ProgramReport(Guid id) => Ok(await status.ProgramReport(CurrentUser, id));

    [HttpGet("{id:guid}/reports/daily.xlsx")]
    public async Task<IActionResult> DailyXlsx(Guid id, [FromQuery] string? from, [FromQuery] string? to)
    {
        var r = await status.DailyReport(CurrentUser, id, from, to);
        return File(ProgramExcel.Daily(r), ProgramExcel.ContentType, ProgramExcel.DailyFileName(r));
    }

    [HttpGet("{id:guid}/reports/program.xlsx")]
    public async Task<IActionResult> ProgramXlsx(Guid id)
    {
        var r = await status.ProgramReport(CurrentUser, id);
        return File(ProgramExcel.Program(r), ProgramExcel.ContentType, ProgramExcel.ProgramFileName(r));
    }
}

[Route("api/feed"), RequireApproved]
public sealed class FeedPostsController(FeedPostService feed) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? sort, [FromQuery] string? kind, [FromQuery] bool mine = false,
        [FromQuery] string? q = null, [FromQuery] string? cursor = null, [FromQuery] int limit = 20, [FromQuery(Name = "program_id")] Guid? programId = null) =>
        Ok(await feed.Page(CurrentUser, programId, FeedPostService.ParseQuery(sort, kind, mine, q, cursor), limit));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) => Ok(await feed.Get(CurrentUser, id));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, FeedPostPatchRequest body) => Ok(await feed.Edit(CurrentUser, id, body));

    [HttpPost]
    public async Task<IActionResult> Create(FeedPostRequest body) => StatusCode(201, await feed.Create(CurrentUser, body));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await feed.Delete(CurrentUser, id);
        return NoContent();
    }

    [HttpPost("{id:guid}/like")]
    public async Task<IActionResult> Like(Guid id) => Ok(await feed.ToggleLike(CurrentUser, id));

    [HttpPost("{id:guid}/comments")]
    public async Task<IActionResult> Comment(Guid id, FeedCommentRequest body) => StatusCode(201, await feed.AddComment(CurrentUser, id, body));

    [HttpDelete("comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteComment(Guid commentId)
    {
        await feed.DeleteComment(CurrentUser, commentId);
        return NoContent();
    }
}
