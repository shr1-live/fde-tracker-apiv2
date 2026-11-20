using System.ComponentModel.DataAnnotations;
using FdeTracker.Api.Filters;
using FdeTracker.Api.Services.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace FdeTracker.Api.Controllers;

public sealed class TaskMessageRequest
{
    string _message = "";
    [Required, StringLength(1000, MinimumLength = 1)] public string Message { get => _message; set => _message = value?.Trim() ?? ""; }
}

public sealed class ConfirmRequest
{
    public required bool Approve { get; set; }
}

/// <summary>The task assistant: talk to it, answer its confirmation questions, and see the tasks and the log. Everything is the signed-in person's own.</summary>
[Route("api/task-assistant"), RequireApproved]
public sealed class TaskAssistantController(TaskAssistantService assistant) : ApiControllerBase
{
    [HttpPost("message")]
    public async Task<IActionResult> Message(TaskMessageRequest body, CancellationToken ct) => Ok(await assistant.Message(CurrentUser, body.Message, ct));

    [HttpPost("confirm/{id:guid}")]
    public async Task<IActionResult> Confirm(Guid id, ConfirmRequest body) => Ok(await assistant.Confirm(CurrentUser, id, body.Approve));

    [HttpGet("tasks")]
    public async Task<IActionResult> Tasks() => Ok(await assistant.Tasks(CurrentUser.Id));

    [HttpGet("log")]
    public async Task<IActionResult> Log() => Ok(await assistant.Log(CurrentUser.Id));
}
