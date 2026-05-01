using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminUsers.Create;

public sealed record CreateAdminUserCommand(
    string Email,
    string FullName,
    string? Phone,
    string RoleId,
    Guid? OrganizationId,
    IReadOnlyList<Guid>? TenantIds,
    string? Password) : IRequest<Result<AdminUserResponse>>;
