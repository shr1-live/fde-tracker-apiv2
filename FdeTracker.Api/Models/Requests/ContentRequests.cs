using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FdeTracker.Api.Models.Requests;

public sealed class CreateContentRequest : IValidatableObject
{
    string _title = "";

    [Required, RegularExpression(ContentPatterns.Topic)] public string Topic { get; set; } = "";
    [RegularExpression(ContentPatterns.Stack)] public string? StackId { get; set; }
    [Required, RegularExpression(ContentPatterns.Kind)] public string Kind { get; set; } = "";
    [Required, StringLength(140, MinimumLength = 3)] public string Title { get => _title; set => _title = value?.Trim() ?? ""; }
    [StringLength(400)] public string? Summary { get; set; }
    [StringLength(20000)] public string? BodyMd { get; set; }
    [StringLength(500), HttpsUrl] public string? Url { get; set; }
    public bool Submit { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (ContentRules.Check(Kind, Url, BodyMd) is { } e) yield return new ValidationResult(e.Message, [e.Field]);
    }
}

/// <summary>Partial update. Fields left out are kept. stack_id may be set to null on purpose, so we track whether it was sent.</summary>
public sealed class PatchContentRequest
{
    string? _stack;
    string? _title;

    [RegularExpression(ContentPatterns.Topic)] public string? Topic { get; set; }
    [RegularExpression(ContentPatterns.Stack)] public string? StackId { get => _stack; set { _stack = value; StackIdSet = true; } }
    [JsonIgnore] public bool StackIdSet { get; private set; }
    [RegularExpression(ContentPatterns.Kind)] public string? Kind { get; set; }
    [StringLength(140, MinimumLength = 3)] public string? Title { get => _title; set => _title = value?.Trim(); }
    [StringLength(400)] public string? Summary { get; set; }
    [StringLength(20000)] public string? BodyMd { get; set; }
    [StringLength(500), HttpsUrl] public string? Url { get; set; }
    public bool? Submit { get; set; }
}

public sealed class FeedQuery
{
    [RegularExpression(ContentPatterns.Topic)] public string? Topic { get; set; }
    [RegularExpression("^(general|dotnet|node|java|python)\\z")] public string? Stack { get; set; }
    [Range(1, 50)] public int Limit { get; set; } = 15;
    /// <summary>Paging cursor. Only used with sort=newest; other sorts return one page and next = null.</summary>
    [StringLength(120)] public string? Before { get; set; }
    /// <summary>newest (default), oldest, title, or recommended (recommended first, then newest).</summary>
    [RegularExpression("^(newest|oldest|title|recommended)\\z")] public string? Sort { get; set; }
}

public sealed class RecommendRequest
{
    public required bool Recommended { get; set; }
}
