using System.Text;

namespace FdeTracker.Api.Services;

/// <summary>
/// Decides what a file really is from its first bytes. The name and the type the browser claims are never trusted:
/// a web page renamed to .png is refused, and every accepted type is served back with a type chosen here.
/// </summary>
public static class FileSniffer
{
    public sealed record Kind(string ContentType, string Extension, bool ShowInline);

    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static Kind? Detect(byte[] b)
    {
        if (b.Length == 0) return null;
        if (Starts(b, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return new("image/png", ".png", true);
        if (Starts(b, 0xFF, 0xD8, 0xFF)) return new("image/jpeg", ".jpg", true);
        if (Starts(b, (byte)'G', (byte)'I', (byte)'F', (byte)'8') && b.Length > 5 && (b[4] == (byte)'7' || b[4] == (byte)'9') && b[5] == (byte)'a') return new("image/gif", ".gif", true);
        if (b.Length > 12 && Starts(b, (byte)'R', (byte)'I', (byte)'F', (byte)'F') && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return new("image/webp", ".webp", true);
        if (Starts(b, (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-')) return new("application/pdf", ".pdf", false);
        if (Starts(b, 0x50, 0x4B, 0x03, 0x04)) return new("application/zip", ".zip", false);
        return IsText(b) ? new("text/plain; charset=utf-8", ".txt", false) : null;
    }

    /// <summary>Plain text only: valid UTF-8 with no control bytes. It is always served as a download, never as a page.</summary>
    static bool IsText(byte[] b)
    {
        if (Array.IndexOf(b, (byte)0) >= 0) return false;
        try { StrictUtf8.GetString(b); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    static bool Starts(byte[] b, params byte[] prefix) => b.Length >= prefix.Length && b.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    /// <summary>A safe display and download name: no folders, no odd characters, at most 100 characters, with the right extension.</summary>
    public static string SafeName(string? original, Kind kind)
    {
        var name = Path.GetFileNameWithoutExtension(original ?? "") ?? "";
        var cleaned = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_').ToArray()).Trim(' ', '_', '.');
        if (cleaned.Length == 0) cleaned = "proof";
        if (cleaned.Length > 90) cleaned = cleaned[..90];
        var ext = Path.GetExtension(original ?? "").ToLowerInvariant();
        // Keep a text-like extension the person chose (.md, .csv, .json, .log); otherwise use the detected one.
        var keep = kind.ContentType.StartsWith("text/", StringComparison.Ordinal) && ext is ".md" or ".csv" or ".json" or ".log" or ".txt";
        return cleaned + (keep ? ext : kind.Extension);
    }
}
