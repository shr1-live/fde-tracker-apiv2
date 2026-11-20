namespace FdeTracker.Api.ViewModels;

public sealed class TaskView
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? DueDate { get; set; }
    public string Priority { get; set; } = "medium";
    public string Status { get; set; } = "open";
    public string? Notes { get; set; }
}

public sealed class ActionView
{
    public string Tool { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string Summary { get; init; } = "";
}

public sealed class PendingView
{
    public Guid Id { get; init; }
    public string Tool { get; init; } = "";
    public string Summary { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
}

/// <summary>What the person sees after a message: the assistant's words, what was done, and anything waiting for their yes or no.</summary>
public sealed class TaskReplyView
{
    public string Reply { get; init; } = "";
    public IReadOnlyList<ActionView> Actions { get; init; } = [];
    public PendingView? Pending { get; init; }
}

public sealed class ToolCallView
{
    public string Tool { get; set; } = "";
    public string? Arguments { get; set; }
    public string Outcome { get; set; } = "";
    public string? Detail { get; set; }
    public int? LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; }
}
