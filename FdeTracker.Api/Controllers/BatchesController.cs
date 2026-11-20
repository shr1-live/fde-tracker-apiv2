using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

[Route("api/admin/batches"), RequireApproved]
public sealed class BatchesController(BatchService batches) : ApiControllerBase
{
    /// <summary>Admin and reviewers may read the list; newest start first.</summary>
    [HttpGet, RequireRole("admin", "reviewer")]
    public async Task<IActionResult> List() => Ok(await batches.List());

    [HttpPost, RequireRole("admin")]
    public async Task<IActionResult> Create(CreateBatchRequest body) => StatusCode(201, await batches.Create(CurrentUser.Id, body));
}
