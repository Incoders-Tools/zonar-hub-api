namespace ZonarHub.Application.Features.Impersonation.Start;

/// <summary>
/// Response shape for a successful impersonation start.
/// Matches design §4.1 response body exactly.
/// </summary>
public sealed record StartImpersonationResponse(
    string Token,
    string TokenType,
    DateTimeOffset ExpiresAt,
    Guid SessionId,
    ImpersonationTargetDto Target);

/// <summary>
/// Minimal target-user projection returned on impersonation start.
/// </summary>
public sealed record ImpersonationTargetDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    Guid? TenantId);
