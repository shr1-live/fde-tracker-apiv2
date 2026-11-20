using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FdeTracker.Api.ViewModels;

namespace FdeTracker.Api.Services;

/// <summary>Builds the two Excel downloads in the layout of the owner's sheets ("FDE 30-Day Daily Status" and "Training Program Status Report").</summary>
public static partial class ProgramExcel
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    static readonly XLColor HeaderFill = XLColor.FromHtml("#1F3864");

    /// <summary>The program name for file names: lowercase, anything but letters and digits becomes "-".</summary>
    public static string Slug(string name)
    {
        var s = NonAlnum().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return s.Length == 0 ? "program" : s.Length > 60 ? s[..60].Trim('-') : s;
    }

    static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string DailyFileName(LpDailyReportView r) => $"{Slug(r.ProgramName)}-daily-{Iso(r.From)}-{Iso(r.To)}.xlsx";
    public static string ProgramFileName(LpProgramReportView r) => $"{Slug(r.ProgramName)}-status-report.xlsx";

    // ---- daily status workbook

    public static byte[] Daily(LpDailyReportView r)
    {
        using var wb = new XLWorkbook();

        var summary = wb.AddWorksheet("Summary");
        Header(summary, 1, ["No.", "Field", "Description"]);
        var row = 2;
        foreach (var (f, i) in r.Fields.Select((f, i) => (f, i)))
        {
            summary.Cell(row, 1).Value = i + 1;
            summary.Cell(row, 2).Value = f.Label;
            summary.Cell(row, 3).Value = f.Description ?? "";
            row++;
        }
        row++;
        Header(summary, row, ["Rating", "Readiness level"]);
        row++;
        foreach (var (line, i) in Rubric.Lines.Select((l, i) => (l, i)))
        {
            summary.Cell(row, 1).Value = i + 1;
            summary.Cell(row, 2).Value = line;
            row++;
        }
        Widths(summary, 10, 45, 70);
        Wrap(summary);
        summary.SheetView.FreezeRows(1);

        // Status: one column per working day counted from the program start (Day 1 = the start), a weekly rating column after every 5 days.
        var status = wb.AddWorksheet("Status");
        var start = r.StartsOn ?? r.From;
        var columns = new List<(string Title, DateOnly? Date)> { ("Name", null) };
        for (var d = r.From; d <= r.To; d = d.AddDays(1))
        {
            if (d < start || d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var n = WorkingDayNumber(start, d);
            columns.Add(($"Day {n}", d));
            if (n % 5 == 0) columns.Add(("Weekly rating", null));
        }
        Header(status, 1, columns.Select(c => c.Title).ToArray());
        var cells = r.Cells.ToDictionary(c => (c.UserId, c.OnDate));
        row = 2;
        foreach (var p in r.People)
        {
            status.Cell(row, 1).Value = p.Name;
            for (var c = 1; c < columns.Count; c++)
                if (columns[c].Date is { } d && cells.TryGetValue((p.UserId, Iso(d)), out var cell))
                    status.Cell(row, c + 1).Value = Lines(r.Fields, cell.Values);
            row++;
        }
        status.Column(1).Width = 24;
        for (var c = 2; c <= columns.Count; c++) status.Column(c).Width = columns[c - 1].Date is null ? 14 : 40;
        Wrap(status);
        status.SheetView.FreezeRows(1);
        status.SheetView.FreezeColumns(1);

        return Save(wb);
    }

    /// <summary>1 for the start day, counting Monday to Friday only (a start on a weekend counts from the next Monday).</summary>
    static int WorkingDayNumber(DateOnly start, DateOnly d)
    {
        var n = 0;
        for (var x = start; x <= d; x = x.AddDays(1))
            if (x.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) n++;
        return n;
    }

    // ---- program status workbook

    public static byte[] Program(LpProgramReportView r)
    {
        using var wb = new XLWorkbook();

        var guide = wb.AddWorksheet("Readiness Guide");
        Header(guide, 1, ["Rating", "Readiness level"]);
        foreach (var (line, i) in Rubric.Lines.Select((l, i) => (l, i)))
        {
            guide.Cell(i + 2, 1).Value = i + 1;
            guide.Cell(i + 2, 2).Value = line;
        }
        Widths(guide, 10, 90);
        Wrap(guide);
        guide.SheetView.FreezeRows(1);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Readiness Guide" };
        var titles = new List<string> { "Week", "Topic", "Topics to Cover", "Practical Task" };
        titles.AddRange(r.Fields.Select(f => f.Label));
        titles.Add("Completed");
        foreach (var p in r.People)
        {
            var ws = wb.AddWorksheet(SheetName(p.Name, used));
            Header(ws, 1, titles.ToArray());
            var rows = p.Rows.ToDictionary(x => x.DayId);
            var row = 2;
            int? lastWeek = null;
            foreach (var d in r.Days)
            {
                if (d.Week != lastWeek) ws.Cell(row, 1).Value = $"Week {d.Week}";
                lastWeek = d.Week;
                ws.Cell(row, 2).Value = $"Day {d.Day}: {d.Topic}";
                ws.Cell(row, 3).Value = d.TopicsToCover;
                ws.Cell(row, 4).Value = d.PracticalTask ?? "";
                rows.TryGetValue(d.Id, out var st);
                for (var i = 0; i < r.Fields.Count; i++)
                    if (st is not null && st.Values.ValueKind == JsonValueKind.Object && st.Values.TryGetProperty(r.Fields[i].Key, out var v))
                        SetValue(ws.Cell(row, 5 + i), v);
                ws.Cell(row, 5 + r.Fields.Count).Value = st?.Completed == true ? "Yes" : "No";
                row++;
            }
            var widths = new List<double> { 10, 30, 60, 60 };
            widths.AddRange(r.Fields.Select(f => f.Type is "rating" or "number" ? 12d : 45d));
            widths.Add(12);
            Widths(ws, widths.ToArray());
            Wrap(ws);
            ws.SheetView.FreezeRows(1);
        }
        return Save(wb);
    }

    /// <summary>Excel sheet names: at most 31 characters, none of []:*?/\ , and unique (ignoring case).</summary>
    static string SheetName(string name, HashSet<string> used)
    {
        var clean = BadSheetChars().Replace(name, " ").Trim().Trim('\'');
        if (clean.Length == 0) clean = "Person";
        var baseName = clean.Length > 31 ? clean[..31] : clean;
        var candidate = baseName;
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = (baseName.Length + suffix.Length > 31 ? baseName[..(31 - suffix.Length)] : baseName) + suffix;
        }
        return candidate;
    }

    // ---- helpers

    static void Header(IXLWorksheet ws, int row, string[] titles)
    {
        for (var i = 0; i < titles.Length; i++) ws.Cell(row, i + 1).Value = titles[i];
        var range = ws.Range(row, 1, row, Math.Max(1, titles.Length));
        range.Style.Fill.BackgroundColor = HeaderFill;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    static void Widths(IXLWorksheet ws, params double[] widths)
    {
        for (var i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
    }

    static void Wrap(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        if (used is null) return;
        used.Style.Alignment.WrapText = true;
        used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    /// <summary>"Label: value" lines, in form order, for the values that are filled in.</summary>
    static string Lines(IReadOnlyList<LpFieldView> fields, JsonElement values)
    {
        var sb = new StringBuilder();
        if (values.ValueKind != JsonValueKind.Object) return "";
        foreach (var f in fields)
        {
            if (!values.TryGetProperty(f.Key, out var v)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(f.Label).Append(": ").Append(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText());
        }
        return sb.ToString();
    }

    static void SetValue(IXLCell cell, JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) cell.Value = d;
        else cell.Value = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }

    static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonAlnum();
    [GeneratedRegex(@"[\[\]:*?/\\]")] private static partial Regex BadSheetChars();
}
