using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FdeTracker.Api.Models.Requests;

public static class SubmissionPatterns
{
    public const string TaskKey = "^[a-z0-9-]{3,60}\\z";
}

/// <summary>What the learner writes about their work: what they did, what they learned, and a 1 to 5 self-rating (the programme sheet's columns).</summary>
public sealed class SaveDetailsRequest
{
    string? _note;
    string? _learned;
    int? _rating;
    // A field that is left out is not changed; a field sent as null is cleared.
    [JsonIgnore] public bool NoteSet { get; private set; }
    [JsonIgnore] public bool LearnedSet { get; private set; }
    [JsonIgnore] public bool RatingSet { get; private set; }
    [StringLength(2000)] public string? Note { get => _note; set { _note = Clean(value); NoteSet = true; } }
    [StringLength(2000)] public string? Learned { get => _learned; set { _learned = Clean(value); LearnedSet = true; } }
    [Range(1, 5)] public int? Rating { get => _rating; set { _rating = value; RatingSet = true; } }
    static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

public sealed class AddLinkRequest
{
    string _label = "";
    string _url = "";
    [Required, StringLength(120, MinimumLength = 1)] public string Label { get => _label; set => _label = value?.Trim() ?? ""; }
    [Required, StringLength(500), HttpsUrl] public string Url { get => _url; set => _url = value?.Trim() ?? ""; }
}

public sealed class ReviewSubmissionRequest
{
    string? _comment;
    [Required, RegularExpression("^(approve|request_changes)\\z")] public string Decision { get; set; } = "";
    [StringLength(2000)] public string? Comment { get => _comment; set => _comment = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
}
