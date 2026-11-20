using FdeTracker.Api.Filters;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>The signed-in person's own notifications. Nobody can read or change another person's.</summary>
[Route("api/notifications"), RequireApproved]
public sealed class NotificationsController(NotificationService notifications) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await notifications.List(CurrentUser));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id) { await notifications.MarkRead(CurrentUser, id); return NoContent(); }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll() { await notifications.MarkAllRead(CurrentUser); return NoContent(); }
}
