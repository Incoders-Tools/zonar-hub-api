using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminUsers.Update;

public sealed record UpdateAdminUserCommand(
    Guid UserId,
    string? FullName,
    string? Phone,
    string? RoleId,
    Guid? OrganizationId,
    IReadOnlyList<Guid>? TenantIds,
    IReadOnlyList<AdminUserOrganizationPermissionInput>? PermissionsByOrganization,
    bool? IsActive) : IRequest<Result<AdminUserResponse>>;
