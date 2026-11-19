using System.Text.Json;
using System.Text.Json.Serialization;

namespace FdeTracker.Api.ViewModels;

// Programs V3 (docs/PROGRAMS-V3.md). Dates are "YYYY-MM-DD" strings. Optional values are always sent (as null), so the web app can rely on every key.

/// <summary>A program in /api/me: the person's role there ("admin", "coordinator" or "member").</summary>
public sealed class MyProgramView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class LpProgramSummaryView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Description { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? StartsOn { get; set; }
    public string MyRole { get; set; } = "";
    public int MemberCount { get; set; }
}

/// <summary>A program row as the repository reads it.</summary>
public sealed class LpProgramRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? StartsOn { get; set; }
    public int MemberCount { get; set; }
    /// <summary>Only set by queries that read the person's membership.</summary>
    public string? Role { get; set; }
}

public sealed class LpDayView
{
    public Guid Id { get; set; }
    public int Week { get; set; }
    public int Day { get; set; }
    public string Topic { get; set; } = "";
    public string TopicsToCover { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? PracticalTask { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Resources { get; set; }
}

public sealed class LpFieldView
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Description { get; set; }
    public string Type { get; set; } = "";
    public bool Required { get; set; }
    /// <summary>"daily" or "day": used inside the API only.</summary>
    [JsonIgnore] public string Form { get; set; } = "";
}

public sealed class LpFormsView
{
    public IReadOnlyList<LpFieldView> Daily { get; init; } = [];
    public IReadOnlyList<LpFieldView> Day { get; init; } = [];
}

public sealed class LpProgramDetailView
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Description { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? StartsOn { get; init; }
    public string MyRole { get; init; } = "";
    public IReadOnlyList<LpDayView> Days { get; init; } = [];
    public LpFormsView Forms { get; init; } = new();
}

public sealed class LpMemberView
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class LpInviteView
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class LpMembersView
{
    public IReadOnlyList<LpMemberView> Members { get; init; } = [];
    public IReadOnlyList<LpInviteView> Invites { get; init; } = [];
}

public sealed class LpInviteResultView { public string Status { get; init; } = ""; }

/// <summary>A status row as the repository reads it: values is the stored JSON text.</summary>
public sealed class LpDailyRow
{
    public Guid UserId { get; set; }
    public string OnDate { get; set; } = "";
    public string ValuesJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}

public sealed class LpDayStatusRow
{
    public Guid UserId { get; set; }
    public Guid DayId { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public bool Completed { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class LpDailyView
{
    public Guid UserId { get; init; }
    public string OnDate { get; init; } = "";
    public JsonElement Values { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static LpDailyView From(LpDailyRow r) => new() { UserId = r.UserId, OnDate = r.OnDate, Values = LpJson.Parse(r.ValuesJson), UpdatedAt = r.UpdatedAt };
}

public sealed class LpDayStatusView
{
    public Guid UserId { get; init; }
    public Guid DayId { get; init; }
    public JsonElement Values { get; init; }
    public bool Completed { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static LpDayStatusView From(LpDayStatusRow r) => new() { UserId = r.UserId, DayId = r.DayId, Values = LpJson.Parse(r.ValuesJson), Completed = r.Completed, UpdatedAt = r.UpdatedAt };
}

public static class LpJson
{
    public static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }

    public static readonly JsonElement Empty = Parse("{}");
}

public sealed class LpPersonView
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class LpDailyCellView
{
    public Guid UserId { get; init; }
    public string OnDate { get; init; } = "";
    public JsonElement Values { get; init; }
}

public sealed class LpDailyReportView
{
    public IReadOnlyList<LpFieldView> Fields { get; init; } = [];
    public IReadOnlyList<LpPersonView> People { get; init; } = [];
    public IReadOnlyList<string> Dates { get; init; } = [];
    public IReadOnlyList<LpDailyCellView> Cells { get; init; } = [];
    /// <summary>The range used, for the export. Not part of the JSON.</summary>
    [JsonIgnore] public DateOnly From { get; init; }
    [JsonIgnore] public DateOnly To { get; init; }
    [JsonIgnore] public string ProgramName { get; init; } = "";
    [JsonIgnore] public DateOnly? StartsOn { get; init; }
}

public sealed class LpReportRowView
{
    public Guid DayId { get; init; }
    public JsonElement Values { get; init; }
    public bool Completed { get; init; }
}

public sealed class LpReportPersonView
{
    public Guid UserId { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public IReadOnlyList<LpReportRowView> Rows { get; init; } = [];
}

public sealed class LpProgramReportView
{
    public IReadOnlyList<LpDayView> Days { get; init; } = [];
    public IReadOnlyList<LpFieldView> Fields { get; init; } = [];
    public IReadOnlyList<LpReportPersonView> People { get; init; } = [];
    [JsonIgnore] public string ProgramName { get; init; } = "";
}

// ---- feed

public sealed class FeedAuthorView
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
}

public sealed class FeedCommentView
{
    public Guid Id { get; init; }
    public FeedAuthorView Author { get; init; } = new();
    public string Body { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public sealed class FeedPostView
{
    public Guid Id { get; init; }
    public FeedAuthorView Author { get; init; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Body { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Url { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>When the author last changed the post; null if never.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public DateTime? EditedAt { get; init; }
    public int LikeCount { get; init; }
    public bool LikedByMe { get; init; }
    public int CommentCount { get; init; }
    public IReadOnlyList<FeedCommentView> Comments { get; init; } = [];
}

/// <summary>One page of the feed. next_cursor is null on the last page.</summary>
public sealed class FeedPostPageView
{
    public IReadOnlyList<FeedPostView> Items { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? NextCursor { get; init; }
}

public sealed class FeedLikeView
{
    public int LikeCount { get; init; }
    public bool LikedByMe { get; init; }
}

/// <summary>Rows as the feed repository reads them.</summary>
public sealed class FeedPostRow
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string? Body { get; set; }
    public string? Url { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public int LikeCount { get; set; }
    public bool LikedByMe { get; set; }
    public int CommentCount { get; set; }
}

public sealed class FeedPostEditRow
{
    public Guid AuthorId { get; set; }
    public string? Body { get; set; }
    public string? Url { get; set; }
}

public sealed class FeedCommentRow
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
