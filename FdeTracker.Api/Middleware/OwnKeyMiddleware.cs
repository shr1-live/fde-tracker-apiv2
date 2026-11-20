using System.Text.RegularExpressions;
using FdeTracker.Api.Common;
using FdeTracker.Api.Services.Llm;

namespace FdeTracker.Api.Middleware;

/// <summary>
/// Lets a signed-in person use their OWN Gemini key for the assistants. The key arrives in the X-Gemini-Key header of that request,
/// is checked for shape, and is kept in memory for that request only. It is read on the assistant routes and nowhere else, it is never
/// stored or logged, and it is never repeated in an error (the messages below are fixed text).
/// </summary>
public sealed class OwnKeyMiddleware(RequestDelegate next)
{
    public const string KeyHeader = "X-Gemini-Key";
    public const string ModelHeader = "X-Gemini-Model";
    const string ModelsPath = "/api/assistant/models";

    // Google API keys come in two shapes: the classic "AIza..." and the newer "AQ...." (letters, digits, "_", "-", "."). Anything else is refused before it goes anywhere.
    static readonly Regex KeyShape = new(@"^(AIza[0-9A-Za-z_\-]{30,60}|AQ\.[0-9A-Za-z_\-.]{20,300})\z", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));
    static readonly Regex ModelShape = new(@"^gemini-[a-z0-9][a-z0-9.\-]{1,50}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    static bool UsesModel(PathString path) =>
        path.StartsWithSegments("/api/assistant", StringComparison.OrdinalIgnoreCase) || path.StartsWithSegments("/api/task-assistant", StringComparison.OrdinalIgnoreCase);

    public async Task Invoke(HttpContext ctx)
    {
        if (!UsesModel(ctx.Request.Path) || !ctx.Request.Headers.TryGetValue(KeyHeader, out var values))
        {
            await next(ctx);
            return;
        }
        if (values.Count != 1 || values[0] is not { } raw || !KeyShape.IsMatch(raw.Trim()))
            throw new ApiException(400, "That does not look like a Gemini API key (they start with AIza or AQ.). Paste the key from Google AI Studio and try again.");

        string? model = null;
        if (ctx.Request.Headers.TryGetValue(ModelHeader, out var m))
        {
            if (m.Count != 1 || m[0] is not { } name || !ModelShape.IsMatch(name.Trim()))
                throw new ApiException(400, "That is not a Gemini model name. Press Test key and choose one from the list.");
            model = name.Trim();
        }
        // The model list is the only call that works before a model is chosen.
        if (model is null && !ctx.Request.Path.Equals(ModelsPath, StringComparison.OrdinalIgnoreCase) && !HttpMethods.IsGet(ctx.Request.Method))
            throw new ApiException(400, "Choose a Gemini model first: press Test key and pick one from the list.");

        using (LlmKeyScope.Use(new OwnKey(raw.Trim(), model))) await next(ctx);
    }
}
