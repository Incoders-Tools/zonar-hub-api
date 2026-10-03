using System.Text;
using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminPermissions.ListPermissionSources;

/// <summary>
/// Searches permission source users. System administrators search every tenant; tenant administrators
/// search only their own tenant without system administrators. Every other caller fails closed.
/// </summary>
public sealed class ListPermissionSourcesHandler
    : IRequestHandler<ListPermissionSourcesQuery, Result<PageResult<PermissionSourceUserResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;

    public ListPermissionSourcesHandler(ICurrentUser currentUser, IUserRepository users)
    {
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result<PageResult<PermissionSourceUserResponse>>> Handle(
        ListPermissionSourcesQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return Result.Failure<PageResult<PermissionSourceUserResponse>>(AdminPermissionErrors.Forbidden);
        }

        var caller = await _users.GetByIdAsync(new UserId(_currentUser.UserId.Value), cancellationToken);
        if (caller is null)
        {
            return Result.Failure<PageResult<PermissionSourceUserResponse>>(AdminPermissionErrors.Forbidden);
        }

        var isSystemAdmin = AdminUserAuthorization.IsSystemAdmin(caller);
        if (!isSystemAdmin && (caller.Role != UserRole.Admin || caller.TenantId is null))
        {
            return Result.Failure<PageResult<PermissionSourceUserResponse>>(AdminPermissionErrors.Forbidden);
        }

        var search = NormalizeSearch(request.Search);
        if (CountMeaningfulCharacters(search) < ListPermissionSourcesLimits.MinSearchLength)
        {
            return Result.Failure<PageResult<PermissionSourceUserResponse>>(ListPermissionSourcesErrors.SearchTooShort);
        }

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > ListPermissionSourcesLimits.MaxPageSize
            ? ListPermissionSourcesLimits.MaxPageSize
            : request.PageSize;

        var query = isSystemAdmin
            ? new UserQuery(null, search, null, null, page, pageSize)
            : new UserQuery(caller.TenantId, search, null, null, page, pageSize, ExcludeRole: UserRole.SystemAdmin);

        var (items, totalCount) = await _users.ListAsync(query, cancellationToken);

        var mapped = items
            .Select(user => new PermissionSourceUserResponse(
                user.Id.Value,
                user.FullName,
                user.Email,
                AdminUserRoleMapper.ToRoleId(user.Role),
                user.IsActive))
            .ToList();

        return Result.Success(new PageResult<PermissionSourceUserResponse>(mapped, page, pageSize, totalCount));
    }

    // Wildcards (*, %) cannot broaden the pattern, and PostgREST logic-tree delimiters (, ( )) plus quoting
    // characters (" \) cannot alter the provider's or=(...) search expression.
    private static string NormalizeSearch(string? search)
    {
        var builder = new StringBuilder((search ?? string.Empty).Length);
        foreach (var character in search ?? string.Empty)
        {
            if (!ListPermissionSourcesLimits.RemovedSearchCharacters.Contains(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }

    // '_' is kept for email searches but, being a single-character wildcard in ilike, never counts as meaningful.
    private static int CountMeaningfulCharacters(string search) =>
        search.Count(character => character != '_' && !char.IsWhiteSpace(character));
}
