namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Exposes the caller's identity to the application layer without coupling to HTTP or Identity frameworks.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Email { get; }
}
