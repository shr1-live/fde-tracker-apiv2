using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>Reviewers and admins: see everyone's submitted work and decide on it. Members get 403 on every route here.</summary>
[Route("api/review/submissions"), RequireApproved, RequireRole("reviewer", "admin")]
public sealed class SubmissionReviewController(SubmissionReviewService reviews) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status) => Ok(await reviews.List(status));

    [HttpGet("counts")]
    public async Task<IActionResult> Counts() => Ok(await reviews.Counts());

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id) => Ok(await reviews.Detail(id));

    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Decide(Guid id, ReviewSubmissionRequest body) => Ok(await reviews.Decide(CurrentUser, id, body.Decision, body.Comment));
}
