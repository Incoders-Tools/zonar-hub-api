namespace ZonarHub.Application.Features.AdminUsers;

public sealed record AdminUserFilter(
    string? Search,
    string? RoleId,
    bool? IsActive,
    int Page,
    int PageSize);
