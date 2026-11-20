using FdeTracker.Api.Models;

namespace FdeTracker.Api.ViewModels;

// The JSON the API returns. Property names become snake_case on the wire.

public sealed class UserView
{
    public Guid Id { get; init; }
    public string Email { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? StackId { get; init; }
    public string Role { get; init; } = "";
    public string Access { get; init; } = "";
    /// <summary>The person's batch, or null when they are not in one. Always sent, so the client can rely on the key.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public BatchView? Batch { get; init; }
    /// <summary>Programs V3: the programs the person is in, with their role there (the admin: every program, as "admin").</summary>
    public IReadOnlyList<MyProgramView> Programs { get; init; } = [];
    /// <summary>The admin, or a coordinator of any program.</summary>
    public bool CanManage { get; init; }

    public static UserView From(AppUser u, BatchView? batch = null, IReadOnlyList<MyProgramView>? programs = null) => new()
    {
        Id = u.Id, Email = u.Email, DisplayName = u.DisplayName, StackId = u.StackId, Role = u.Role, Access = u.AccessStatus, Batch = batch,
        Programs = programs ?? [], CanManage = u.IsAdmin || (programs ?? []).Any(p => p.Role == "coordinator"),
    };
}

/// <summary>A batch: a group of learners who start on the same day. starts_on is "YYYY-MM-DD".</summary>
public sealed class BatchView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string StartsOn { get; set; } = "";
}

public sealed class BatchListItemView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string StartsOn { get; set; } = "";
    public int Members { get; set; }
}

public sealed class BatchAssignView { public Guid Id { get; set; } public string Email { get; set; } = ""; public Guid? BatchId { get; set; } }

public sealed class ProfileView
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? StackId { get; set; }
    public string Role { get; set; } = "";
}

public sealed class AdminUserView
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? StackId { get; set; }
    public string Role { get; set; } = "";
    public string AccessStatus { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public Guid? BatchId { get; set; }
    public string? BatchName { get; set; }
}

public sealed class AccessChangeView { public Guid Id { get; set; } public string Email { get; set; } = ""; public string AccessStatus { get; set; } = ""; }
public sealed class RoleChangeView { public Guid Id { get; set; } public string Email { get; set; } = ""; public string Role { get; set; } = ""; }

public sealed class StackView { public string Id { get; set; } = ""; public string Label { get; set; } = ""; }
public sealed class TopicView { public string Slug { get; set; } = ""; public string Title { get; set; } = ""; public string? Description { get; set; } }

public sealed class FeedItemView
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string? Summary { get; init; }
    public string Kind { get; init; } = "";
    public string? Url { get; init; }
    public string? StackId { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string Topic { get; init; } = "";
    public string TopicTitle { get; init; } = "";
    public string Author { get; init; } = "";
    public bool Recommended { get; init; }

    public static FeedItemView From(ContentRecord c) => new()
    {
        Id = c.Id, Title = c.Title, Summary = c.Summary, Kind = c.Kind, Url = c.Url, StackId = c.StackId,
        ApprovedAt = c.ApprovedAt, Topic = c.Topic, TopicTitle = c.TopicTitle, Author = c.Author, Recommended = c.Recommended,
    };
}

public sealed class FeedPageView { public IReadOnlyList<FeedItemView> Items { get; init; } = []; public string? Next { get; init; } }

/// <summary>One item in full. Deliberately has no author id: internal ids are never exposed.</summary>
public sealed class ContentDetailView
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string? Summary { get; init; }
    public string Kind { get; init; } = "";
    public string? Url { get; init; }
    public string? StackId { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string Topic { get; init; } = "";
    public string TopicTitle { get; init; } = "";
    public string Author { get; init; } = "";
    public string? BodyMd { get; init; }
    public string Status { get; init; } = "";
    public bool Recommended { get; init; }

    public static ContentDetailView From(ContentRecord c) => new()
    {
        Id = c.Id, Title = c.Title, Summary = c.Summary, Kind = c.Kind, Url = c.Url, StackId = c.StackId, ApprovedAt = c.ApprovedAt,
        Topic = c.Topic, TopicTitle = c.TopicTitle, Author = c.Author, BodyMd = c.BodyMd, Status = c.Status, Recommended = c.Recommended,
    };
}

public sealed class MyContentView
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string? StackId { get; set; }
    public string Topic { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public string? LastReviewComment { get; set; }
}

public sealed class ContentSavedView { public Guid Id { get; set; } public string Status { get; set; } = ""; }

public sealed class ReviewQueueItemView
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Url { get; set; }
    public string? Summary { get; set; }
    public string? StackId { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string Topic { get; set; } = "";
    public string TopicTitle { get; set; } = "";
    public string Author { get; set; } = "";
}

public sealed class ReviewResultView { public string Status { get; init; } = ""; }

public sealed class ProgramSummaryView
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Visibility { get; set; } = "";
    public int Total { get; set; }
    public int Done { get; set; }
}

public sealed class ProgramItemView
{
    public Guid Id { get; set; }
    public int Position { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ContentId { get; set; }
    public bool Done { get; set; }
}

public sealed class ProgramDetailView
{
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string Visibility { get; init; } = "";
    public bool Mine { get; init; }
    public IReadOnlyList<ProgramItemView> Items { get; init; } = [];
}

public sealed class ProgramCreatedView { public string Slug { get; set; } = ""; public string Title { get; set; } = ""; }
public sealed class NewItemView { public Guid Id { get; set; } public int Position { get; set; } public string Title { get; set; } = ""; public string? Description { get; set; } }
public sealed class ProgressView { public bool Done { get; init; } }
