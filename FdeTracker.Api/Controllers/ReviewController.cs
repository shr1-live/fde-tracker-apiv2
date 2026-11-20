using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

[Route("api/review"), RequireApproved, RequireRole("reviewer", "admin")]
public sealed class ReviewController(ReviewService reviews) : ApiControllerBase
{
    [HttpGet("queue")]
    public async Task<IActionResult> Queue() => Ok(await reviews.Queue());

    [HttpPost("{id}")]
    public async Task<IActionResult> Decide(Guid id, ReviewDecisionRequest body) => Ok(await reviews.Decide(CurrentUser, id, body));
}
