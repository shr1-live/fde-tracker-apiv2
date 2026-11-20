using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

[Route("api/admin/users"), RequireApproved, RequireRole("admin")]
public sealed class AdminController(UserService users) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await users.List());

    [HttpPost("{id}/access")]
    public async Task<IActionResult> SetAccess(Guid id, SetAccessRequest body) => Ok(await users.SetAccess(CurrentUser.Id, id, body.Status));

    [HttpPost("{id}/role")]
    public async Task<IActionResult> SetRole(Guid id, SetRoleRequest body) => Ok(await users.SetRole(CurrentUser.Id, id, body.Role));

    [HttpPost("{id}/batch")]
    public async Task<IActionResult> SetBatch(Guid id, SetBatchRequest body) => Ok(await users.SetBatch(CurrentUser.Id, id, body.BatchId));
}
