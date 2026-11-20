using System.Globalization;
using System.Text;
using FdeTracker.Api.Common;

namespace FdeTracker.Api.Models;

public enum FeedSort { New, Top, Discussed }

/// <summary>Where the previous page stopped: the last row's sort values and id.</summary>
public sealed record FeedCursor(FeedSort Sort, int Count, DateTime At, Guid Id)
{
    /// <summary>Opaque to callers: base64url of "sort|count|ticks|id".</summary>
    public string Encode()
    {
        var raw = string.Join('|', Sort.ToString().ToLowerInvariant(), Count.ToString(CultureInfo.InvariantCulture),
            At.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), Id.ToString("N"));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Reads a cursor made by <see cref="Encode"/> for the same sort; anything else is a 400.</summary>
    public static FeedCursor Decode(string value, FeedSort sort)
    {
        var bad = ApiException.BadRequest("cursor is not valid; start again from the first page");
        if (value.Length is 0 or > 200) throw bad;
        string raw;
        try
        {
            var b64 = value.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            raw = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(b64));
        }
        catch (Exception e) when (e is FormatException or ArgumentException) { throw bad; }
        var parts = raw.Split('|');
        if (parts.Length != 4 || parts[0] != sort.ToString().ToLowerInvariant()) throw bad;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count)) throw bad;
        if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) throw bad;
        if (!Guid.TryParseExact(parts[3], "N", out var id)) throw bad;
        if (sort == FeedSort.New && count != 0) throw bad;
        return new FeedCursor(sort, count, new DateTime(ticks, DateTimeKind.Utc), id);
    }
}

/// <summary>A checked feed query. Kind is "video", "link", "text" or null (all); Pattern is a ready ILIKE pattern with wildcards escaped.</summary>
public sealed record FeedPostQuery(FeedSort Sort, string? Kind, bool Mine, string? Pattern, FeedCursor? After);
