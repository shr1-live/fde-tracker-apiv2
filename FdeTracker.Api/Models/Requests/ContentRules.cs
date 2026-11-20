using System.ComponentModel.DataAnnotations;

namespace FdeTracker.Api.Models.Requests;

public static class ContentPatterns
{
    public const string Topic = "^[a-z0-9-]{2,40}\\z";
    public const string Stack = "^(dotnet|node|java|python)\\z";
    public const string Kind = "^(article|video|link|note)\\z";
}

public static class ContentRules
{
    /// <summary>Links need a url, text kinds need a body. Returns the field and message, or null when fine.</summary>
    public static (string Field, string Message)? Check(string? kind, string? url, string? body)
    {
        if (kind is "video" or "link" && string.IsNullOrEmpty(url)) return ("url", "A link is required for this kind");
        if (kind is "article" or "note" && string.IsNullOrWhiteSpace(body)) return ("body_md", "Text is required for this kind");
        return null;
    }
}

/// <summary>
/// Links must be absolute https URLs with a real host and no username or password in them. A link such as
/// https://github.com@evil.example shows one name and goes to another, and a password in a link would be stored in the database.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HttpsUrlAttribute : ValidationAttribute
{
    public HttpsUrlAttribute() : base("Links must use https and must not contain a username or password") { }

    public static bool IsAllowed(string s) =>
        s.StartsWith("https://", StringComparison.Ordinal) && Uri.TryCreate(s, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps && u.Host.Length > 0 && u.UserInfo.Length == 0 && !s.Any(char.IsWhiteSpace);

    protected override ValidationResult? IsValid(object? value, ValidationContext context) =>
        value is null or "" || (value is string s && IsAllowed(s))
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage, [context.MemberName ?? "url"]);
}
