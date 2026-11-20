using FdeTracker.Api.Models;

namespace FdeTracker.Api.ViewModels;

public sealed class SubmissionItemView
{
    public Guid Id { get; init; }
    public string Kind { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Url { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public int? SizeBytes { get; init; }
    public bool Previewable { get; init; }

    public static SubmissionItemView From(SubmissionItemRow r) => new()
    {
        Id = r.Id, Kind = r.Kind, Label = r.Label, Url = r.Url, FileName = r.FileName, ContentType = r.ContentType, SizeBytes = r.SizeBytes,
        Previewable = r.ContentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp",
    };
}

public sealed class SubmissionReviewView
{
    public string Decision { get; set; } = "";
    public string? Comment { get; set; }
    public string Reviewer { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>One submission in full: what the person handed in, and everything reviewers said about it.</summary>
public sealed class SubmissionView
{
    public Guid Id { get; init; }
    public string TaskKey { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Note { get; init; }
    public string? Learned { get; init; }
    public int? Rating { get; init; }
    public string Author { get; init; } = "";
    public DateTime? SubmittedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public bool Editable { get; init; }
    public IReadOnlyList<SubmissionItemView> Items { get; init; } = [];
    public IReadOnlyList<SubmissionReviewView> Reviews { get; init; } = [];
}

/// <summary>A row in a list: the person's own submissions, or the reviewers' queue.</summary>
public sealed class SubmissionListView
{
    public Guid Id { get; set; }
    public string TaskKey { get; set; } = "";
    public string Status { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime? SubmittedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int ItemCount { get; set; }
    public string? LastComment { get; set; }
}

public sealed class SubmissionCountsView
{
    public int Submitted { get; set; }
    public int ChangesRequested { get; set; }
    public int Approved { get; set; }
    public int Drafts { get; set; }
}
