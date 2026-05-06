namespace ZonarHub.Application.Features.AdminPermissions;

public sealed record PermissionCatalogResponse(
    IReadOnlyList<PermissionModuleResponse> Modules);

public sealed record PermissionModuleResponse(
    string Key,
    string LabelKey,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<PermissionToolResponse> Tools);

public sealed record PermissionToolResponse(
    string Key,
    string LabelKey,
    string? Route,
    int SortOrder,
    bool IsSystemAdminOnly,
    bool IsActive);

public sealed record AdminUserPermissionsResponse(
    Guid UserId,
    IReadOnlyList<UserOrganizationPermissionsResponse> PermissionsByOrganization);

public sealed record UserOrganizationPermissionsResponse(
    Guid OrganizationId,
    IReadOnlyList<string> ToolKeys);

public sealed record EffectivePermissionsResponse(
    Guid? OrganizationId,
    IReadOnlyList<string> ToolKeys);
