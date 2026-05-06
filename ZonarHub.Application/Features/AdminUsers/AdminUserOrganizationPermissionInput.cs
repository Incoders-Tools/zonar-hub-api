namespace ZonarHub.Application.Features.AdminUsers;

public sealed record AdminUserOrganizationPermissionInput(
    Guid OrganizationId,
    IReadOnlyList<string> ToolKeys);
