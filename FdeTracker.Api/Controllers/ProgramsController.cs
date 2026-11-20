using System.ComponentModel.DataAnnotations;
using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>The old personal plans ("My plan"). Moved from /api/programs to /api/plans when Programs V3 took that path.</summary>
[Route("api/plans"), RequireApproved]
public sealed class ProgramsController(ProgramService programs) : ApiControllerBase
{
    const string SlugPattern = "^[a-z0-9-]{2,60}\\z";

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await programs.List(CurrentUser.Id));

    [HttpPost]
    public async Task<IActionResult> Create(CreateProgramRequest body) => StatusCode(201, await programs.Create(CurrentUser.Id, body));

    [HttpGet("{slug}")]
    public async Task<IActionResult> Detail([RegularExpression(SlugPattern)] string slug) => Ok(await programs.Detail(CurrentUser.Id, slug));

    [HttpPost("{slug}/items")]
    public async Task<IActionResult> AddItem([RegularExpression(SlugPattern)] string slug, AddItemRequest body) =>
        StatusCode(201, await programs.AddItem(CurrentUser.Id, slug, body));

    [HttpDelete("{slug}/items/{itemId}")]
    public async Task<IActionResult> RemoveItem([RegularExpression(SlugPattern)] string slug, Guid itemId)
    {
        await programs.RemoveItem(CurrentUser.Id, slug, itemId);
        return NoContent();
    }

    [HttpPut("{slug}/items/{itemId}/progress")]
    public async Task<IActionResult> SetProgress([RegularExpression(SlugPattern)] string slug, Guid itemId, SetProgressRequest body) =>
        Ok(await programs.SetProgress(CurrentUser.Id, slug, itemId, body.Done));
}
