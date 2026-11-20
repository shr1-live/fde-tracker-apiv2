using System.Security.Claims;
using System.Text.Json;
using FdeTracker.Api.Common;
using FdeTracker.Api.Models;
using FdeTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace FdeTracker.Api.Middleware;

public static class HttpContextUser
{
    internal const string Key = "fde.user";
    public static AppUser GetUser(this HttpContext ctx) => (AppUser)ctx.Items[Key]!;
}

/// <summary>Writes the standard error body: { "error": "..." }.</summary>
public static class ErrorBody
{
    public static Task Write(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { error = message });
    }

    /// <summary>The same body, plus the category, kind, retry flag and wait when the error carries them. Old callers that only read "error" are not affected.</summary>
    public static Task Write(HttpContext ctx, ApiException e)
    {
        if (e.Category is null) return Write(ctx, e.Status, e.Message);
        ctx.Response.StatusCode = e.Status;
        if (e.RetryAfterSeconds is { } s) ctx.Response.Headers.RetryAfter = ((int)Math.Ceiling(s)).ToString();
        return ctx.Response.WriteAsJsonAsync(new { error = e.Message, category = e.Category, retryable = e.Retryable ?? false, kind = e.Kind, retry_after_seconds = e.RetryAfterSeconds });
    }
}

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task Invoke(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (ApiException e) when (!ctx.Response.HasStarted) { await ErrorBody.Write(ctx, e); }
        catch (BadHttpRequestException e) when (!ctx.Response.HasStarted)
        {
            await ErrorBody.Write(ctx, e.StatusCode, e.StatusCode == 413 ? "Request too large" : "Invalid request");
        }
        catch (JsonException) when (!ctx.Response.HasStarted) { await ErrorBody.Write(ctx, 400, "Invalid JSON"); }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled error");
            if (!ctx.Response.HasStarted) await ErrorBody.Write(ctx, 500, "Something went wrong");
        }
    }
}

/// <summary>Adds the usual hardening headers and caps request bodies at 64 KB.</summary>
public sealed class SecurityMiddleware(RequestDelegate next, IHostEnvironment env)
{
    public const long MaxBody = 64 * 1024;
    public const long UploadMaxBody = 2_800_000; // one 2 MB file plus the form wrapper
    public const long LabRunMaxBody = 80 * 1024; // a saved lab run: a result of up to 64 KB plus its name and model list

    /// <summary>Only the submission file upload and a saved lab run may exceed 64 KB; every other route keeps the small cap.</summary>
    static readonly System.Text.RegularExpressions.Regex UploadRoute = new(@"^/api/submissions/[a-z0-9-]{3,60}/files\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    static long LimitFor(HttpRequest request) =>
        !HttpMethods.IsPost(request.Method) ? MaxBody
        : UploadRoute.IsMatch(request.Path.Value ?? "") ? UploadMaxBody
        : request.Path.Equals("/api/lab/runs", StringComparison.OrdinalIgnoreCase) ? LabRunMaxBody
        : MaxBody;

    static bool IsDocsPage(PathString path) =>
        path == "/" || path.StartsWithSegments("/scalar") || path.StartsWithSegments("/openapi");

    public Task Invoke(HttpContext ctx)
    {
        var limit = LimitFor(ctx.Request);
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        // JSON only: nothing may load or frame it. The one exception is the Development-only docs page, which needs its scripts.
        if (!(env.IsDevelopment() && IsDocsPage(ctx.Request.Path))) h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        if (!env.IsDevelopment()) h["Strict-Transport-Security"] = "max-age=15552000; includeSubDomains"; // local http stays usable
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        if (ctx.Request.ContentLength > limit) return ErrorBody.Write(ctx, 413, "Request too large");
        if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } f) f.MaxRequestBodySize = limit;
        return next(ctx);
    }
}

/// <summary>After authentication: checks the account is on the allowed domain and loads (or creates) the app user.</summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext ctx, UserService users, AppOptions options)
    {
        var needsUser = ctx.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null && ctx.User.Identity?.IsAuthenticated == true;
        if (!needsUser) { await next(ctx); return; }

        // Supabase marks an address it has not confirmed with email_verified=false; such a token never signs anyone in.
        if (string.Equals(ctx.User.FindFirstValue("email_verified"), "false", StringComparison.OrdinalIgnoreCase))
        {
            await ErrorBody.Write(ctx, 403, "Confirm your email address first");
            return;
        }
        var email = (ctx.User.FindFirstValue("email") ?? "").ToLowerInvariant();
        if (!Guid.TryParseExact(ctx.User.FindFirstValue("sub"), "D", out var id) || !email.EndsWith("@" + options.AllowedDomain, StringComparison.Ordinal))
        {
            await ErrorBody.Write(ctx, 403, "This account is not allowed");
            return;
        }
        ctx.Items[HttpContextUser.Key] = await users.SignIn(id, email);
        await next(ctx);
    }
}
