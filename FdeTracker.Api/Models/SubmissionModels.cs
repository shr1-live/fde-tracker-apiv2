namespace FdeTracker.Api.Models;

public sealed class SubmissionRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TaskKey { get; set; } = "";
    public string? Note { get; set; }
    public string? Learned { get; set; }
    public int? Rating { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Only a draft, or a submission sent back with changes requested, can be edited by its owner.</summary>
    public bool IsEditable => Status is "draft" or "changes_requested";
}

/// <summary>A proof without its file bytes: safe to list.</summary>
public sealed class SubmissionItemRow
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Url { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public int? SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A stored file with the owner, for downloads.</summary>
public sealed class StoredFile
{
    public Guid OwnerId { get; set; }
    public string Status { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = [];
}
