using System.ComponentModel.DataAnnotations;
using FdeTracker.Api.Common;
using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>A person's own submissions. Every route works on the signed-in person's data only; the task key is the handle, never someone else's id.</summary>
[Route("api/submissions"), RequireApproved]
public sealed class SubmissionsController(SubmissionService submissions) : ApiControllerBase
{
    const string Key = SubmissionPatterns.TaskKey;

    [HttpGet]
    public async Task<IActionResult> Mine() => Ok(await submissions.Mine(CurrentUser.Id));

    [HttpGet("{taskKey}")]
    public async Task<IActionResult> Get([RegularExpression(Key)] string taskKey) => Ok(await submissions.Get(CurrentUser.Id, taskKey));

    // Reviewers review; they do not submit. Every route that changes a submission is for learners (members) and admins.
    [HttpPut("{taskKey}"), RequireRole("member", "admin")]
    public async Task<IActionResult> SaveDetails([RegularExpression(Key)] string taskKey, SaveDetailsRequest body) => Ok(await submissions.SaveDetails(CurrentUser, taskKey, body));

    [HttpPost("{taskKey}/links"), RequireRole("member", "admin")]
    public async Task<IActionResult> AddLink([RegularExpression(Key)] string taskKey, AddLinkRequest body) => Ok(await submissions.AddLink(CurrentUser, taskKey, body.Label, body.Url));

    /// <summary>One file per request, up to 2 MB. The size limit for this route is raised in SecurityMiddleware; all other routes stay at 64 KB.</summary>
    [HttpPost("{taskKey}/files"), RequireRole("member", "admin"), RequestFormLimits(MultipartBodyLengthLimit = 2_800_000)]
    public async Task<IActionResult> AddFile([RegularExpression(Key)] string taskKey, [FromForm] IFormFile file, [FromForm, StringLength(120)] string? label)
    {
        if (file.Length > SubmissionService.MaxFileBytes) throw new ApiException(413, "Files can be up to 2 MB");
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return Ok(await submissions.AddFile(CurrentUser, taskKey, label, file.FileName, buffer.ToArray()));
    }

    [HttpDelete("{taskKey}/items/{itemId:guid}"), RequireRole("member", "admin")]
    public async Task<IActionResult> DeleteItem([RegularExpression(Key)] string taskKey, Guid itemId) => Ok(await submissions.DeleteItem(CurrentUser, taskKey, itemId));

    [HttpPost("{taskKey}/withdraw"), RequireRole("member", "admin")]
    public async Task<IActionResult> Withdraw([RegularExpression(Key)] string taskKey) => Ok(await submissions.Withdraw(CurrentUser, taskKey));

    [HttpPost("{taskKey}/submit"), RequireRole("member", "admin")]
    public async Task<IActionResult> Submit([RegularExpression(Key)] string taskKey) => Ok(await submissions.Submit(CurrentUser, taskKey));

    /// <summary>Owner, reviewers and admins only. Always served with the type we detected, never as a web page.</summary>
    [HttpGet("items/{itemId:guid}/file")]
    public async Task<IActionResult> Download(Guid itemId)
    {
        var file = await submissions.Download(CurrentUser, itemId);
        var inline = file.ContentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        Response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition { DispositionType = inline ? "inline" : "attachment", FileName = file.FileName }.ToString();
        return File(file.Data, file.ContentType);
    }
}
