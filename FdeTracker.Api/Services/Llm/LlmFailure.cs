using FdeTracker.Api.Common;

namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// Turns a failure of the model provider into the error the caller gets: a status, our own wording (never the provider's text or the key),
/// the category, and whether trying again later may help. One place, so every endpoint answers the same way.
/// </summary>
public static class LlmFailure
{
    /// <summary>When the call used the person's own key, a refused key or a missing model is theirs to fix, not the administrator's, and the message says so.</summary>
    public static ApiException ToApi(LlmException e, bool ownKey = false)
    {
        var wait = e.RetryAfter is { } w ? Math.Ceiling(w.TotalSeconds) : (double?)null;
        var (status, message) = e.Kind switch
        {
            "timeout" => (504, "The model took too long. Try again."),
            "rate_limited" when ownKey => (429, $"Google is limiting your Gemini key (free keys have low per-minute and per-day limits). Try again in {wait ?? 30} seconds."),
            "rate_limited" => (429, $"The model provider is limiting requests right now. Try again in {wait ?? 30} seconds."),
            "server_error" => (502, "The model provider had an error. Try again later."),
            "network" => (503, "Could not reach the model provider. Try again shortly."),
            "auth" when ownKey => (400, "Gemini refused your key. Check that it is copied in full and that the Gemini API is enabled for it, then paste it again."),
            "auth" => (502, "The server's model credentials were refused. This is a setup problem, not something you did: tell the administrator."),
            "bad_request" or "too_large" => (502, "The model could not accept this request. It may be too long: shorten it and try again."),
            "model_not_found" when ownKey => (400, "Gemini does not have the model you chose. Press Test key and pick one from the list."),
            "model_not_found" => (502, "The configured model was not found. Tell the administrator."),
            "content_blocked" => (422, "The model declined to answer this request."),
            _ => (502, "The model service is not available. Try again later."),
        };
        return new ApiException(status, message) { Category = e.Category.ToString().ToLowerInvariant(), Kind = e.Kind, Retryable = e.Retryable, RetryAfterSeconds = e.Kind == "rate_limited" ? wait ?? 30 : null };
    }
}
