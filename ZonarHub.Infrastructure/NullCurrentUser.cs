using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure;

/// <summary>
/// Default <see cref="ICurrentUser"/> when authentication is not configured.
/// Replaced by the Identity-backed implementation when auth is enabled.
/// </summary>
internal sealed class NullCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public string? Email => null;

    public Guid? OrganizationId => null;
}
