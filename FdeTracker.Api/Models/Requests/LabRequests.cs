using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace FdeTracker.Api.Models.Requests;

/// <summary>A practice-lab run to keep. The result is any JSON object or list the web app built; it is stored and returned as data only.</summary>
public sealed class SaveLabRunRequest : IValidatableObject
{
    string _label = "";
    [Range(1, 3, ErrorMessage = "Day must be 1, 2 or 3")] public int Day { get; set; }
    [Required, StringLength(120, MinimumLength = 1, ErrorMessage = "Give the run a name of 1 to 120 characters")] public string Label { get => _label; set => _label = value?.Trim() ?? ""; }
    [Required, MinLength(1, ErrorMessage = "Name at least one model"), MaxLength(4, ErrorMessage = "At most 4 models")] public List<string> Models { get; set; } = [];
    public JsonElement Result { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Label.Any(char.IsControl)) yield return new ValidationResult("The name cannot contain control characters", [nameof(Label)]);
        if (Models.Any(m => m is null || m.Trim().Length is 0 or > 80 || m.Any(char.IsControl)))
            yield return new ValidationResult("Each model name must be 1 to 80 characters", [nameof(Models)]);
        if (Result.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            yield return new ValidationResult("The result must be a JSON object or list", [nameof(Result)]);
    }
}
