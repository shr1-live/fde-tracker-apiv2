namespace FdeTracker.Api.Models;

/// <summary>A content item joined with its topic and author name. Internal: never returned as-is.</summary>
public sealed class ContentRecord
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string Kind { get; set; } = "";
    public string? Url { get; set; }
    public string? StackId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string Topic { get; set; } = "";
    public string TopicTitle { get; set; } = "";
    public string Author { get; set; } = "";
    public string? BodyMd { get; set; }
    public string Status { get; set; } = "";
    public Guid AuthorId { get; set; }
    public bool Recommended { get; set; }
}

/// <summary>The stored columns the edit rules need.</summary>
public sealed class ContentRow
{
    public Guid Id { get; set; }
    public int TopicId { get; set; }
    public string? StackId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? BodyMd { get; set; }
    public string? Url { get; set; }
    public string Status { get; set; } = "";
    public Guid AuthorId { get; set; }
}

public sealed class ProgramRecord
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Visibility { get; set; } = "";
    public Guid? OwnerId { get; set; }
}

public sealed class ReviewTarget
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public string Status { get; set; } = "";
}
