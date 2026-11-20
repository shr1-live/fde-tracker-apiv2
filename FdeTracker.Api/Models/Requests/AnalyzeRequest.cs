using System.ComponentModel.DataAnnotations;

namespace FdeTracker.Api.Models.Requests;

public sealed class CandidateInput
{
    string _name = "";
    string _profile = "";
    [Required, StringLength(80, MinimumLength = 1)] public string Name { get => _name; set => _name = value?.Trim() ?? ""; }
    [Required, StringLength(4000, MinimumLength = 20)] public string Profile { get => _profile; set => _profile = value?.Trim() ?? ""; }
}

public sealed class AnalyzeRequest : IValidatableObject
{
    string _jobDescription = "";
    [Required, StringLength(6000, MinimumLength = 20)] public string JobDescription { get => _jobDescription; set => _jobDescription = value?.Trim() ?? ""; }
    [Required, MinLength(1), MaxLength(5)] public List<CandidateInput> Candidates { get; set; } = [];
    [RegularExpression("^v[1-7]\\z")] public string? PromptVersion { get; set; }

    // A body like {"candidates":[null]} passes the length checks, so refuse null entries here.
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Candidates.Any(c => c is null)) yield return new ValidationResult("Every candidate needs a name and a profile", [nameof(Candidates)]);
    }
}
