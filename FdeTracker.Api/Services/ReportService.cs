using FdeTracker.Api.Common;
using FdeTracker.Api.Data;
using FdeTracker.Api.Models;
using FdeTracker.Api.Repositories;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

/// <summary>Progress reports per person. Read-only, so nothing is written to the audit log.
/// A learner reads only their own report; reviewers and the admin read anyone's (the controller enforces the roles).</summary>
public sealed class ReportService(Db db, ReportRepository repo)
{
    public Task<List<PersonRowView>> People(Guid? batch) => db.Read(s => repo.People(s, batch));

    public Task<PersonReportView> For(AppUser viewer, Guid personId) => db.Read(async s =>
    {
        var person = await repo.Person(s, personId) ?? throw ApiException.NotFound();
        var days = await repo.Days(s, personId);
        days.Sort((a, b) => NaturalKey.Compare(a.TaskKey, b.TaskKey));

        // Only handed-in days count: a draft's rating is private to its owner, so it must not be readable through the average either.
        var rated = days.Where(d => d.Rating is not null && d.Status != "draft").Select(d => (double)d.Rating!.Value).ToList();
        var totals = new ReportTotalsView
        {
            Submitted = days.Count(d => d.Status == "submitted"),
            Approved = days.Count(d => d.Status == "approved"),
            ChangesRequested = days.Count(d => d.Status == "changes_requested"),
            Drafts = days.Count(d => d.Status == "draft"),
            AvgRating = rated.Count == 0 ? null : Math.Round(rated.Average(), 1, MidpointRounding.AwayFromZero),
        };

        // Drafts stay private to their owner: someone else sees that a draft exists, not what is in it.
        if (viewer.Id != personId)
            foreach (var d in days.Where(d => d.Status == "draft")) { d.Note = null; d.Learned = null; d.Rating = null; }

        return new PersonReportView { Person = person, Days = days, Totals = totals };
    });
}

/// <summary>Orders task keys the way people read them: week1-day2 before week1-day10.</summary>
public static class NaturalKey
{
    public static int Compare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                var c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                if (a[i] != b[j]) return a[i].CompareTo(b[j]);
                i++; j++;
            }
        }
        var rest = (a.Length - i).CompareTo(b.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(a, b);
    }
}
