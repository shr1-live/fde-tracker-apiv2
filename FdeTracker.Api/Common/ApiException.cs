namespace FdeTracker.Api.Common;

/// <summary>An error that carries the HTTP status to send. Services throw it; the error middleware renders it.</summary>
public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    /// <summary>Optional extras for failures of the model provider: what kind of problem it is, whether trying again may help, and how long to wait.</summary>
    public string? Category { get; init; }
    public string? Kind { get; init; }
    public bool? Retryable { get; init; }
    public double? RetryAfterSeconds { get; init; }
    public static ApiException NotFound(string message = "Not found") => new(404, message);
    public static ApiException BadRequest(string message) => new(400, message);
}
