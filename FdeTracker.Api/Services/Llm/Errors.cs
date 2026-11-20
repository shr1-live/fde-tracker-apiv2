namespace FdeTracker.Api.Services.Llm;

/// <summary>
/// What kind of problem it is, which decides what we do and what we tell the caller.
/// Whether trying again can help is a separate question: see <see cref="LlmException.Retryable"/>.
/// </summary>
public enum ErrorCategory
{
    /// <summary>The request itself is wrong (bad or too-large input). Trying the same thing again cannot help.</summary>
    Validation,
    /// <summary>Credentials or access are refused. A setup problem for the administrator, not the user.</summary>
    Permission,
    /// <summary>A rule says no: the model is not found, the answer was blocked. Not fixed by retrying.</summary>
    Business,
    /// <summary>The provider or the network failed for now (timeout, 5xx, cannot connect). Usually passes.</summary>
    Transient,
    /// <summary>The provider is limiting requests (429). Passes after the wait it asks for.</summary>
    RateLimit,
}

/// <summary>
/// A failure from the model provider, already sorted. The message is safe to show and log: it never holds the key or the provider's own reply.
/// </summary>
public sealed class LlmException : Exception
{
    public string Kind { get; }
    public ErrorCategory Category { get; }
    /// <summary>True when the same request may work if tried again later.</summary>
    public bool Retryable { get; }
    /// <summary>How long the provider asked us to wait, when it said.</summary>
    public TimeSpan? RetryAfter { get; }

    public LlmException(string kind, ErrorCategory category, bool retryable, string message, TimeSpan? retryAfter = null) : base(message)
    {
        Kind = kind; Category = category; Retryable = retryable; RetryAfter = retryAfter;
    }

    /// <summary>For callers that only know a kind: it is sorted by the kind's name.</summary>
    public LlmException(string kind, string message) : this(kind, CategoryOf(kind), RetryableKind(kind), message) { }

    static ErrorCategory CategoryOf(string kind) => kind switch
    {
        "timeout" or "network" or "server_error" or "bad_format" => ErrorCategory.Transient,
        "rate_limited" => ErrorCategory.RateLimit,
        "auth" => ErrorCategory.Permission,
        "bad_request" or "too_large" => ErrorCategory.Validation,
        _ => ErrorCategory.Business,
    };

    static bool RetryableKind(string kind) => CategoryOf(kind) is ErrorCategory.Transient or ErrorCategory.RateLimit;

    public static LlmException Timeout(int seconds) => new("timeout", ErrorCategory.Transient, true, $"No answer within {seconds} seconds");
    public static LlmException Network() => new("network", ErrorCategory.Transient, true, "Could not reach the provider");
    public static LlmException BadFormat() => new("bad_format", ErrorCategory.Transient, true, "The provider's answer was not in the expected format");
    public static LlmException Blocked() => new("content_blocked", ErrorCategory.Business, false, "The provider declined to answer this request");

    /// <summary>Sorts a provider's HTTP status into a category. Only the number is used, never the provider's text.</summary>
    public static LlmException FromStatus(int code, TimeSpan? retryAfter = null) => code switch
    {
        400 => new("bad_request", ErrorCategory.Validation, false, "The provider could not accept this request"),
        413 => new("too_large", ErrorCategory.Validation, false, "The request was too large for the provider"),
        401 or 403 => new("auth", ErrorCategory.Permission, false, "The provider refused our credentials"),
        404 => new("model_not_found", ErrorCategory.Business, false, "The configured model was not found at the provider"),
        408 => new("timeout", ErrorCategory.Transient, true, "The provider timed out"),
        429 => new("rate_limited", ErrorCategory.RateLimit, true, "The provider is limiting requests", retryAfter),
        >= 500 => new("server_error", ErrorCategory.Transient, true, $"The provider answered {code}"),
        _ => new("provider_error", ErrorCategory.Business, false, $"The provider answered {code}"),
    };
}
