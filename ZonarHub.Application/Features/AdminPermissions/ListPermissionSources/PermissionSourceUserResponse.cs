namespace ZonarHub.Application.Features.AdminPermissions.ListPermissionSources;

/// <summary>
/// Minimal user projection used to pick a permission source; organization metadata is intentionally omitted.
/// </summary>
/// <param name="Id">User identifier.</param>
/// <param name="FullName">User display name.</param>
/// <param name="Email">User email.</param>
/// <param name="RoleId">Stable role identifier (for example <c>role002</c>).</param>
/// <param name="IsActive">Whether the user is active; inactive users remain valid sources.</param>
public sealed record PermissionSourceUserResponse(
    Guid Id,
    string FullName,
    string Email,
    string RoleId,
    bool IsActive);
