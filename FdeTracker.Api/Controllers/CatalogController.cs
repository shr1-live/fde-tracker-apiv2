using FdeTracker.Api.Data;
using FdeTracker.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>Public endpoints: health check and the fixed lists the UI needs before sign-in.</summary>
[ApiController]
public sealed class CatalogController(Db db, CatalogRepository catalog) : ControllerBase
{
    [HttpGet("/health")]
    public async Task<IActionResult> Health()
    {
        await db.Read(catalog.Ping);
        return Ok(new { ok = true });
    }

    [HttpGet("/api/stacks")]
    public async Task<IActionResult> Stacks() => Ok(await db.Read(catalog.Stacks));

    [HttpGet("/api/topics")]
    public async Task<IActionResult> Topics() => Ok(await db.Read(catalog.Topics));
}
