namespace FdeTracker.Api.Models;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? StackId { get; set; }
    public string Role { get; set; } = "member";
    public string AccessStatus { get; set; } = "pending";
    public Guid? BatchId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    public bool IsApproved => AccessStatus == "approved";
    public bool IsAdmin => Role == "admin";
}
