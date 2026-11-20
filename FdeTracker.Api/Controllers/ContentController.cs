using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

[RequireApproved]
public sealed class ContentController(ContentService content) : ApiControllerBase
{
    /// <summary>Approved content only; general items for everyone plus the chosen stack's items. Moved from /api/feed when Programs V3 took that path.</summary>
    [HttpGet("content/feed")]
    public async Task<IActionResult> Feed([FromQuery] FeedQuery query) => Ok(await content.Feed(CurrentUser, query));

    [HttpGet("content/mine")]
    public async Task<IActionResult> Mine() => Ok(await content.Mine(CurrentUser.Id));

    [HttpGet("content/{id}")]
    public async Task<IActionResult> Get(Guid id) => Ok(await content.Get(CurrentUser, id));

    [HttpPost("content")]
    public async Task<IActionResult> Create(CreateContentRequest body) => StatusCode(201, await content.Create(CurrentUser, body));

    [HttpPatch("content/{id}")]
    public async Task<IActionResult> Update(Guid id, PatchContentRequest body) => Ok(await content.Update(CurrentUser, id, body));

    /// <summary>The review team marks an approved item as recommended, or takes the mark off.</summary>
    [HttpPost("content/{id}/recommend"), RequireRole("reviewer", "admin")]
    public async Task<IActionResult> Recommend(Guid id, RecommendRequest body) => Ok(await content.Recommend(CurrentUser, id, body.Recommended));

    [HttpDelete("content/{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await content.Delete(CurrentUser.Id, id);
        return NoContent();
    }
}
