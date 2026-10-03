using MediatR;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminUsers.GetAll;

/// <summary>
/// Lists managed users within an explicit scope.
/// </summary>
/// <param name="Filter">Search, role, activation, and paging filters.</param>
/// <param name="Scope">
/// <see cref="AdminUserListScopes.Organization"/> (default when omitted) or <see cref="AdminUserListScopes.All"/>.
/// </param>
/// <param name="OrganizationId">Selected organization; required for organization scope and rejected for all scope.</param>
public sealed record GetAdminUsersQuery(
    AdminUserFilter Filter,
    string? Scope = null,
    Guid? OrganizationId = null)
    : IRequest<Result<PageResult<AdminUserResponse>>>;

/// <summary>
/// Stable, machine-readable values accepted by the admin users list <c>scope</c> parameter.
/// </summary>
public static class AdminUserListScopes
{
    /// <summary>Users associated with the selected organization (default).</summary>
    public const string Organization = "organization";

    /// <summary>Every user across organizations; system administrators only.</summary>
    public const string All = "all";
}
