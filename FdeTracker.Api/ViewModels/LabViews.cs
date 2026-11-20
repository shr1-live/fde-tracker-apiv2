using System.Text.Json;

namespace FdeTracker.Api.ViewModels;

public sealed class LabCaseView { public string Id { get; init; } = ""; public string Kind { get; init; } = ""; public string Prompt { get; init; } = ""; }

public sealed class LabCasesView
{
    /// <summary>The system prompt every Day 1 case is sent with.</summary>
    public string System { get; init; } = "";
    public List<LabCaseView> Cases { get; init; } = [];
}

public sealed class LabDay1ResultView
{
    public string CaseId { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Model { get; init; } = "";
    public bool Passed { get; init; }
    /// <summary>The answer as scored, cut to <see cref="Services.Lab.LabService.MaxAnswerChars"/> characters for display.</summary>
    public string Answer { get; init; } = "";
    public int LatencyMs { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}

public sealed class LabDay2CandidateView
{
    public string Name { get; init; } = "";
    /// <summary>The model's rating; empty when it gave none for this candidate.</summary>
    public string Fit { get; init; } = "";
    public string[] ExpectedFit { get; init; } = [];
    /// <summary>Skills this candidate must NOT be credited with (the profile does not support them).</summary>
    public string[] ForbiddenClaims { get; init; } = [];
    /// <summary>The skills the model credited (after the quote check).</summary>
    public string[] Skills { get; init; } = [];
    public bool Passed { get; init; }
    public string Note { get; init; } = "";
}

public sealed class LabDay2ResultView
{
    public string Version { get; init; } = "";
    public string Model { get; init; } = "";
    public int Batch { get; init; }
    public int Batches { get; init; }
    public int LatencyMs { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public List<LabDay2CandidateView> Candidates { get; init; } = [];
    public int Passed { get; init; }
    public int Total { get; init; }
    public List<string> Warnings { get; init; } = [];
}

/// <summary>A saved lab run. The result is the JSON the person saved, returned as data exactly as stored.</summary>
public sealed class LabRunView
{
    public Guid Id { get; init; }
    public int Day { get; init; }
    public string Label { get; init; } = "";
    public string[] Models { get; init; } = [];
    public JsonElement Result { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsSample { get; init; }
}

/// <summary>A pinned sample run: no owner information.</summary>
public sealed class LabSampleView
{
    public Guid Id { get; init; }
    public int Day { get; init; }
    public string Label { get; init; } = "";
    public string[] Models { get; init; } = [];
    public JsonElement Result { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class LabRunsView { public List<LabRunView> Runs { get; init; } = []; }
public sealed class LabSamplesView { public List<LabSampleView> Samples { get; init; } = []; }
