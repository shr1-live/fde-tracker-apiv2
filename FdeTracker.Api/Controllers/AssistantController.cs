using FdeTracker.Api.Filters;
using FdeTracker.Api.Models.Requests;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

/// <summary>Recruitment candidate analysis with an LLM. Approved users only, with a daily cap per person.</summary>
[Route("api/assistant"), RequireApproved]
public sealed class AssistantController(AssistantService assistant) : ApiControllerBase
{
    [HttpGet("usage")]
    public async Task<IActionResult> Usage() => Ok(await assistant.Usage(CurrentUser.Id));

    /// <summary>Models the person's own Gemini key can use. Send the key in the X-Gemini-Key header.</summary>
    [HttpGet("models")]
    public async Task<IActionResult> Models(CancellationToken ct) => Ok(await assistant.Models(ct));

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(AnalyzeRequest body, CancellationToken ct) => Ok(await assistant.Analyze(CurrentUser.Id, body, ct));
}
