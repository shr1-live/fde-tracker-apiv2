using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FdeTracker.Api.Models.Requests;

public sealed class UpdateMeRequest
{
    string? _stack;
    [StringLength(80)] public string? DisplayName { get; set; }
    [RegularExpression(ContentPatterns.Stack)] public string? StackId { get => _stack; set { _stack = value; StackIdSet = true; } }
    [JsonIgnore] public bool StackIdSet { get; private set; }
}

public sealed class SetAccessRequest
{
    [Required, RegularExpression("^(approved|rejected|pending)\\z")] public string Status { get; set; } = "";
}

public sealed class SetRoleRequest
{
    [Required, RegularExpression("^(member|reviewer)\\z")] public string Role { get; set; } = "";
}

public sealed class CreateBatchRequest : IValidatableObject
{
    static readonly DateOnly Earliest = new(2020, 1, 1), Latest = new(2100, 12, 31);
    string _name = "";

    [Required, StringLength(60, MinimumLength = 2)] public string Name { get => _name; set => _name = value?.Trim() ?? ""; }
    /// <summary>"YYYY-MM-DD", a real calendar date between 2020-01-01 and 2100-12-31.</summary>
    [Required, RegularExpression("^[0-9]{4}-[0-9]{2}-[0-9]{2}\\z")] public string StartsOn { get; set; } = "";

    // A method, not a property: model validation reads every property, and this one only works once the date is valid.
    public DateOnly StartsOnDate() => DateOnly.ParseExact(StartsOn, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!DateOnly.TryParseExact(StartsOn, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            || d < Earliest || d > Latest)
            yield return new ValidationResult("starts_on must be a real date between 2020-01-01 and 2100-12-31", [nameof(StartsOn)]);
    }
}

/// <summary>batch_id must be sent: a uuid puts the person in that batch, null takes them out of any batch.</summary>
public sealed class SetBatchRequest : IValidatableObject
{
    Guid? _batch;
    public Guid? BatchId { get => _batch; set { _batch = value; BatchIdSet = true; } }
    [JsonIgnore] public bool BatchIdSet { get; private set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!BatchIdSet) yield return new ValidationResult("batch_id is required (use null to remove the person from their batch)", [nameof(BatchId)]);
    }
}

public sealed class ReviewDecisionRequest
{
    string? _comment;
    [Required, RegularExpression("^(approve|request_changes|reject)\\z")] public string Decision { get; set; } = "";
    [StringLength(2000)] public string? Comment { get => _comment; set => _comment = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
}

public sealed class CreateProgramRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Title { get; set; } = "";
    [StringLength(600)] public string? Description { get; set; }
}

public sealed class AddItemRequest
{
    [Required, StringLength(140, MinimumLength = 1)] public string Title { get; set; } = "";
    [StringLength(600)] public string? Description { get; set; }
}

public sealed class SetProgressRequest
{
    public required bool Done { get; set; }
}
