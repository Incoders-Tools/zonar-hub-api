using MediatR;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminPermissions.ListPermissionSources;

/// <summary>
/// Searches users whose permissions the caller may copy from.
/// </summary>
/// <param name="Search">
/// Name or email fragment. <see cref="ListPermissionSourcesLimits.RemovedSearchCharacters"/> are removed, then at
/// least <see cref="ListPermissionSourcesLimits.MinSearchLength"/> characters other than <c>_</c> and whitespace
/// must remain.
/// </param>
/// <param name="Page">One-based page number; values below 1 are normalized to 1.</param>
/// <param name="PageSize">Page size; capped at <see cref="ListPermissionSourcesLimits.MaxPageSize"/>.</param>
public sealed record ListPermissionSourcesQuery(
    string? Search,
    int Page = 1,
    int PageSize = ListPermissionSourcesLimits.MaxPageSize)
    : IRequest<Result<PageResult<PermissionSourceUserResponse>>>;

/// <summary>
/// Bounds applied to permission source searches so they cannot enumerate users broadly.
/// </summary>
public static class ListPermissionSourcesLimits
{
    /// <summary>Minimum meaningful search length after trimming and removing wildcards.</summary>
    public const int MinSearchLength = 2;

    /// <summary>Maximum page size; also the default.</summary>
    public const int MaxPageSize = 20;

    /// <summary>
    /// Characters removed from the search before it reaches persistence: wildcards (<c>*</c>, <c>%</c>) so they
    /// cannot broaden matching, and delimiters/quoting (<c>,</c>, <c>(</c>, <c>)</c>, <c>"</c>, <c>\</c>) so they
    /// cannot alter a provider filter expression such as PostgREST <c>or=(...)</c>.
    /// </summary>
    /// <remarks>
    /// <c>_</c> is preserved because it is common in emails, even though it is a single-character wildcard for
    /// SQL <c>LIKE</c>/<c>ILIKE</c> providers. To bound that tradeoff it never counts toward
    /// <see cref="MinSearchLength"/>, so <c>__</c> or <c>_a_</c> are rejected while <c>ana_lopez</c> is allowed.
    /// </remarks>
    public static readonly IReadOnlySet<char> RemovedSearchCharacters =
        new HashSet<char> { '*', '%', ',', '(', ')', '"', '\\' };
}

/// <summary>
/// Errors specific to the permission source search.
/// </summary>
public static class ListPermissionSourcesErrors
{
    public static readonly Error SearchTooShort = Error.Validation(
        "admin_permissions.source_search_too_short",
        "admin.permissions.errors.source_search_too_short");
}
