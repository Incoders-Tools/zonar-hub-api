namespace ZonarHub.Application.Features.Auth;

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    AuthUserDto User,
    AuthTenantDto? Tenant);

public sealed record AuthUserDto(
    string Id,
    string Email,
    string FullName,
    string RoleId,
    string Role,
    bool IsActive,
    string CreatedAt,
    string? Phone,
    string? BirthDate,
    string? AvatarUrl,
    string? TenantId,
    string[]? TenantIds,
    string? OrganizationId,
    string? Locale,
    string? DateFormat);

public sealed record AuthTenantDto(
    string Id,
    string Name,
    string Key,
    string ContactEmail,
    string PlanId,
    string PlanType);
