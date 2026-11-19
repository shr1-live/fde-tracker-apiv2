using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FdeTracker.Api.Models.Requests;

// Programs V3 (docs/PROGRAMS-V3.md). Text is trimmed; an empty optional text counts as "not given".

static class Text
{
    public static string? Opt(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    public static string Req(string? v) => v?.Trim() ?? "";

    /// <summary>Parses "YYYY-MM-DD" as a real calendar date.</summary>
    public static bool TryDate(string? v, out DateOnly d) =>
        DateOnly.TryParseExact(v ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
}

public sealed class CreateLpProgramRequest : IValidatableObject
{
    string _name = "";
    string? _description, _startsOn;

    [Required, StringLength(80, MinimumLength = 2)] public string Name { get => _name; set => _name = Text.Req(value); }
    [StringLength(600)] public string? Description { get => _description; set => _description = Text.Opt(value); }
    public string? StartsOn { get => _startsOn; set => _startsOn = Text.Opt(value); }

    public DateOnly? StartsOnDate() => Text.TryDate(StartsOn, out var d) ? d : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartsOn is not null && !LpDates.IsValidStart(StartsOn))
            yield return new ValidationResult("starts_on must be a real date between 2020-01-01 and 2100-12-31", [nameof(StartsOn)]);
    }
}

/// <summary>Only the fields sent are changed. description and starts_on may be sent as null to clear them.</summary>
public sealed class PatchLpProgramRequest : IValidatableObject
{
    string? _name, _description, _startsOn;

    [StringLength(80, MinimumLength = 2)] public string? Name { get => _name; set { _name = value?.Trim(); NameSet = true; } }
    [StringLength(600)] public string? Description { get => _description; set { _description = Text.Opt(value); DescriptionSet = true; } }
    public string? StartsOn { get => _startsOn; set { _startsOn = Text.Opt(value); StartsOnSet = true; } }

    [JsonIgnore] public bool NameSet { get; private set; }
    [JsonIgnore] public bool DescriptionSet { get; private set; }
    [JsonIgnore] public bool StartsOnSet { get; private set; }

    public DateOnly? StartsOnDate() => Text.TryDate(StartsOn, out var d) ? d : null;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (NameSet && string.IsNullOrEmpty(Name)) yield return new ValidationResult("name cannot be empty", [nameof(Name)]);
        if (StartsOn is not null && !LpDates.IsValidStart(StartsOn))
            yield return new ValidationResult("starts_on must be a real date between 2020-01-01 and 2100-12-31", [nameof(StartsOn)]);
    }
}

public static class LpDates
{
    static readonly DateOnly Earliest = new(2020, 1, 1), Latest = new(2100, 12, 31);
    public static bool IsValidStart(string v) => Text.TryDate(v, out var d) && d >= Earliest && d <= Latest;
}

public sealed class CreateLpDayRequest
{
    string _topic = "", _cover = "";
    string? _practical, _resources;

    [Range(1, 52)] public required int Week { get; set; }
    [Range(1, 7)] public required int Day { get; set; }
    [Required, StringLength(140, MinimumLength = 1)] public string Topic { get => _topic; set => _topic = Text.Req(value); }
    [StringLength(4000)] public string TopicsToCover { get => _cover; set => _cover = value?.Trim() ?? ""; }
    [StringLength(4000)] public string? PracticalTask { get => _practical; set => _practical = Text.Opt(value); }
    [StringLength(2000)] public string? Resources { get => _resources; set => _resources = Text.Opt(value); }
}

/// <summary>Only the fields sent are changed. practical_task and resources may be sent as null to clear them.</summary>
public sealed class PatchLpDayRequest : IValidatableObject
{
    string? _topic, _cover, _practical, _resources;

    [Range(1, 52)] public int? Week { get; set; }
    [Range(1, 7)] public int? Day { get; set; }
    [StringLength(140)] public string? Topic { get => _topic; set { _topic = value?.Trim(); TopicSet = true; } }
    [StringLength(4000)] public string? TopicsToCover { get => _cover; set { _cover = value?.Trim() ?? ""; CoverSet = true; } }
    [StringLength(4000)] public string? PracticalTask { get => _practical; set { _practical = Text.Opt(value); PracticalSet = true; } }
    [StringLength(2000)] public string? Resources { get => _resources; set { _resources = Text.Opt(value); ResourcesSet = true; } }

    [JsonIgnore] public bool TopicSet { get; private set; }
    [JsonIgnore] public bool CoverSet { get; private set; }
    [JsonIgnore] public bool PracticalSet { get; private set; }
    [JsonIgnore] public bool ResourcesSet { get; private set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (TopicSet && string.IsNullOrEmpty(Topic)) yield return new ValidationResult("topic cannot be empty", [nameof(Topic)]);
    }
}

public sealed class LpFieldInput
{
    string _key = "", _label = "";
    string? _description;

    [Required, RegularExpression("^[a-z][a-z0-9_]{1,39}\\z")] public string Key { get => _key; set => _key = value ?? ""; }
    [Required, StringLength(80, MinimumLength = 1)] public string Label { get => _label; set => _label = Text.Req(value); }
    [StringLength(400)] public string? Description { get => _description; set => _description = Text.Opt(value); }
    [Required, RegularExpression("^(text|longtext|number|rating|url)\\z")] public string Type { get; set; } = "";
    public required bool Required { get; set; }
}

public sealed class SaveLpFormRequest : IValidatableObject
{
    [Required, MinLength(1), MaxLength(20)] public List<LpFieldInput> Fields { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != Fields.Count)
            yield return new ValidationResult("Field keys must be unique", [nameof(Fields)]);
    }
}

public sealed class LpInviteRequest
{
    string _email = "";

    [Required, StringLength(254, MinimumLength = 3), EmailAddress] public string Email { get => _email; set => _email = value?.Trim().ToLowerInvariant() ?? ""; }
    [Required, RegularExpression("^(coordinator|member)\\z")] public string Role { get; set; } = "";
}

public sealed class LpRoleRequest
{
    [Required, RegularExpression("^(coordinator|member)\\z")] public string Role { get; set; } = "";
}

public sealed class LpDailyRequest
{
    public JsonElement Values { get; set; }
}

public sealed class LpDayStatusRequest
{
    public JsonElement Values { get; set; }
    public required bool Completed { get; set; }
}

public sealed class FeedPostRequest
{
    string? _body, _url;

    [StringLength(2000)] public string? Body { get => _body; set => _body = Text.Opt(value); }
    [StringLength(500), RegularExpression("^https://\\S+\\z", ErrorMessage = "url must start with https://")] public string? Url { get => _url; set => _url = Text.Opt(value); }
}

/// <summary>Changes a post. A field left out keeps its value; a field sent as null or empty is cleared.</summary>
public sealed class FeedPostPatchRequest
{
    string? _body, _url;

    [StringLength(2000)] public string? Body { get => _body; set { _body = Text.Opt(value); BodyGiven = true; } }
    [StringLength(500), RegularExpression("^https://\\S+\\z", ErrorMessage = "url must start with https://")] public string? Url { get => _url; set { _url = Text.Opt(value); UrlGiven = true; } }
    [JsonIgnore] public bool BodyGiven { get; private set; }
    [JsonIgnore] public bool UrlGiven { get; private set; }
}

public sealed class FeedCommentRequest
{
    string _body = "";
    [Required, StringLength(1000, MinimumLength = 1)] public string Body { get => _body; set => _body = Text.Req(value); }
}
