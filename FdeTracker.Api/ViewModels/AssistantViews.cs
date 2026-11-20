namespace FdeTracker.Api.ViewModels;

public sealed class MatchedSkillView { public string Skill { get; init; } = ""; public string Evidence { get; init; } = ""; }

/// <summary>What our own code found in the profile, independent of the model.</summary>
public sealed class SkillCheckView { public List<string> Matched { get; init; } = []; public List<string> Missing { get; init; } = []; public int Score { get; init; } }

public sealed class CandidateAnalysisView
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<MatchedSkillView> MatchedSkills { get; init; } = [];
    /// <summary>Claims dropped because their quote was not in the profile.</summary>
    public int RemovedClaims { get; init; }
    public List<string> Gaps { get; init; } = [];
    public List<string> NotStated { get; init; } = [];
    public string Fit { get; init; } = "";
    /// <summary>True when the profile contained text that gives orders to the assistant (it was ignored).</summary>
    public bool InstructionsFound { get; init; }
    /// <summary>The model's short reasoning, evidence first (prompts v5 to v7). Empty when not asked for, when the profile gave orders, or when the summary is built in code.</summary>
    public string Reasoning { get; init; } = "";
    /// <summary>Questions to ask the candidate when the profile is too thin or vague (from the model, or built in code when the summary is).</summary>
    public List<string> ClarifyingQuestions { get; init; } = [];
    public SkillCheckView Check { get; init; } = new();
}

public sealed class AnalysisView
{
    public string PromptVersion { get; init; } = "";
    public string Model { get; init; } = "";
    public int LatencyMs { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public List<string> RequiredSkills { get; init; } = [];
    public List<CandidateAnalysisView> Candidates { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class AssistantUsageView
{
    public bool Configured { get; init; }
    public string? Model { get; init; }
    public int DailyCap { get; init; }
    /// <summary>True when this request carried the person's own Gemini key (the shared limit does not apply to it).</summary>
    public bool UsingOwnKey { get; init; }
    public int UsedToday { get; init; }
    public string[] PromptVersions { get; init; } = [];
}
