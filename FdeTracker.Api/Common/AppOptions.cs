namespace FdeTracker.Api.Common;

/// <summary>Settings read from environment variables (set in the Render dashboard in production).</summary>
public sealed class AppOptions
{
    public string DatabaseUrl { get; init; } = "";
    public string SupabaseUrl { get; init; } = "";
    public string? SupabaseJwtSecret { get; init; }
    public string AdminEmail { get; init; } = "shivanshu.rai@fiftyfivetech.io";
    public string AllowedDomain { get; init; } = "fiftyfivetech.io";
    public string[] CorsOrigins { get; init; } = [];

    // The assistant is optional: without all three LLM_* values it answers 503 and nothing else is affected.
    public string? LlmBaseUrl { get; init; }
    public string? LlmApiKey { get; init; }
    public string? LlmModel { get; init; }
    public string LlmProvider { get; init; } = "openai"; // "openai" = any chat-completions provider, "anthropic" = Claude
    public int LlmDailyCap { get; init; } = 20;
    public int LlmTimeoutSeconds { get; init; } = 30;
    public int LlmRetryDelayMs { get; init; } = 1000;
    /// <summary>Overall deadline for one model call, every attempt and wait included.</summary>
    public int LlmTotalTimeoutSeconds { get; init; } = 60;
    /// <summary>Tries per call for failures that can pass (429, 5xx, network).</summary>
    public int LlmMaxAttempts { get; init; } = 3;
    /// <summary>The longest wait a provider may ask for (Retry-After) that we will sit through. Longer, and the caller is told how long to wait.</summary>
    public int LlmMaxRetryAfterSeconds { get; init; } = 30;
    /// <summary>One task-assistant message may take this long in all (up to four model calls). When it runs out, the reply says what was done so far.</summary>
    public int TaskMessageDeadlineSeconds { get; init; } = 90;
    /// <summary>The longest any single database command may run.</summary>
    public int DbCommandTimeoutSeconds { get; init; } = 20;
    /// <summary>How the client waits between attempts. Tests replace it so they do not really wait.</summary>
    public Func<TimeSpan, CancellationToken, Task> Sleep { get; init; } = Task.Delay;
    /// <summary>A number from 0 to 1 that spreads retries out. Tests replace it to get exact waits.</summary>
    public Func<double> Jitter { get; init; } = Random.Shared.NextDouble;
    /// <summary>Daily status reminders: on/off, the local hour they go out, and the company time zone (IANA name).</summary>
    public bool RemindersEnabled { get; init; } = true;
    public int ReminderHour { get; init; } = 10;
    public string ReminderTimeZone { get; init; } = "Asia/Kolkata";
    /// <summary>Optional reminder email through Resend (free tier). Without both values only the in-app reminder is made.</summary>
    public string? ResendApiKey { get; init; }
    public string? EmailFrom { get; init; }
    /// <summary>The website address used in email links.</summary>
    public string AppUrl { get; init; } = "https://fde-tracker-green.vercel.app";
    public int RateLimitPerMinute { get; init; } = 240; // all requests per address
    public int WriteLimitPerMinute { get; init; } = 40; // changes per person
    public int LlmMaxTokens { get; init; } = 3000;
    /// <summary>0 = the most repeatable answers (right for checking and extraction). Higher = more varied, and more likely to invent.</summary>
    public double LlmTemperature { get; init; } = 0;
    /// <summary>Optional: only the most likely words that add up to this share are considered. Left unset unless you have a reason.</summary>
    public double? LlmTopP { get; init; }
    /// <summary>"model" = the model's own summary text (default). "code" = a summary built in our code from the checked fields.</summary>
    public string AssistantSummary { get; init; } = "model"; // longest answer we pay for; raise it if several candidates get cut off

    static string ProviderOf(string? value)
    {
        var p = (value ?? "openai").ToLowerInvariant();
        return Services.Llm.LlmClientFactory.Providers.Contains(p) ? p : throw new InvalidOperationException($"LLM_PROVIDER must be one of: {string.Join(", ", Services.Llm.LlmClientFactory.Providers)}");
    }

    public static AppOptions From(IConfiguration c)
    {
        string Need(string key) => c[key] is { Length: > 0 } v ? v : throw new InvalidOperationException($"Missing required setting {key}");
        string? Opt(string key) => c[key] is { Length: > 0 } v ? v : null;
        // A decimal setting inside a range. Missing or out of range means the default: a typo must not silently turn the model wild.
        double? Decimal(string key, double fallback, double min, double max) =>
            double.TryParse(c[key], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d >= min && d <= max ? d : fallback;
        int Num(string key, int fallback) => int.TryParse(c[key], out var n) && n > 0 ? n : fallback;
        var provider = ProviderOf(Opt("LLM_PROVIDER"));
        return new AppOptions
        {
            DatabaseUrl = Need("DATABASE_URL"),
            SupabaseUrl = Need("SUPABASE_URL"),
            SupabaseJwtSecret = Opt("SUPABASE_JWT_SECRET"),
            AdminEmail = (c["ADMIN_EMAIL"] ?? "shivanshu.rai@fiftyfivetech.io").ToLowerInvariant(),
            AllowedDomain = (c["ALLOWED_DOMAIN"] ?? "fiftyfivetech.io").ToLowerInvariant(),
            CorsOrigins = (c["CORS_ORIGINS"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            LlmBaseUrl = Opt("LLM_BASE_URL"),
            LlmApiKey = Opt("LLM_API_KEY"),
            LlmModel = Opt("LLM_MODEL"),
            LlmProvider = provider,
            LlmDailyCap = Num("LLM_DAILY_CAP", 20),
            LlmTimeoutSeconds = Num("LLM_TIMEOUT_SECONDS", 30),
            LlmRetryDelayMs = Num("LLM_RETRY_DELAY_MS", 1000),
            LlmTotalTimeoutSeconds = Num("LLM_TOTAL_TIMEOUT_SECONDS", 60),
            LlmMaxAttempts = Num("LLM_MAX_ATTEMPTS", 3),
            LlmMaxRetryAfterSeconds = Num("LLM_MAX_RETRY_AFTER_SECONDS", 30),
            TaskMessageDeadlineSeconds = Num("TASK_MESSAGE_DEADLINE_SECONDS", 90),
            DbCommandTimeoutSeconds = Num("DB_COMMAND_TIMEOUT_SECONDS", 20),
            LlmMaxTokens = Num("LLM_MAX_TOKENS", 3000),
            LlmTemperature = Decimal("LLM_TEMPERATURE", 0, 0, provider == "anthropic" ? 1 : 2) ?? 0,   // Claude accepts 0 to 1, the common API 0 to 2
            LlmTopP = double.TryParse(c["LLM_TOP_P"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var topP) && topP >= 0 && topP <= 1 ? topP : null, // unset, or anything out of range, means "not sent"
            AssistantSummary = string.Equals(c["ASSISTANT_SUMMARY"], "code", StringComparison.OrdinalIgnoreCase) ? "code" : "model",
            RemindersEnabled = !string.Equals(c["REMINDERS_ENABLED"], "false", StringComparison.OrdinalIgnoreCase),
            ReminderHour = int.TryParse(c["REMINDER_HOUR"], out var rh) && rh is >= 0 and <= 23 ? rh : 10,
            ReminderTimeZone = Opt("REMINDER_TIME_ZONE") ?? "Asia/Kolkata",
            ResendApiKey = Opt("RESEND_API_KEY"),
            EmailFrom = Opt("EMAIL_FROM"),
            AppUrl = Opt("APP_URL") is { } appUrl && appUrl.StartsWith("https://", StringComparison.Ordinal) ? appUrl : "https://fde-tracker-green.vercel.app",
            RateLimitPerMinute = Num("RATE_LIMIT_PER_MINUTE", 240),
            WriteLimitPerMinute = Num("WRITE_LIMIT_PER_MINUTE", 40),
        };
    }
}
