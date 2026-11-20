namespace FdeTracker.Api.ViewModels;

/// <summary>One row of the reviewers' "people" list: how far a person has got.</summary>
public sealed class PersonRowView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? BatchName { get; set; }
    public string? BatchStartsOn { get; set; }
    public int Submitted { get; set; }
    public int Approved { get; set; }
    public int ChangesRequested { get; set; }
    public int Drafts { get; set; }
    public double? AvgRating { get; set; }
    public DateTime? LastActivity { get; set; }
}

/// <summary>Who a report is about. Never carries the email address.</summary>
public sealed class ReportPersonView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public BatchView? Batch { get; set; }
}

/// <summary>One day (one submission) in a person's report, with the newest review decision.</summary>
public sealed class ReportDayView
{
    public string TaskKey { get; set; } = "";
    public string Status { get; set; } = "";
    public int? Rating { get; set; }
    public string? Learned { get; set; }
    public string? Note { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int ItemCount { get; set; }
    public string? LastDecision { get; set; }
    public string? LastComment { get; set; }
    public string? LastReviewer { get; set; }
}

public sealed class ReportTotalsView
{
    public int Submitted { get; init; }
    public int Approved { get; init; }
    public int ChangesRequested { get; init; }
    public int Drafts { get; init; }
    public double? AvgRating { get; init; }
}

public sealed class PersonReportView
{
    public ReportPersonView Person { get; init; } = new();
    public IReadOnlyList<ReportDayView> Days { get; init; } = [];
    public ReportTotalsView Totals { get; init; } = new();
}
