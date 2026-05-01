using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;
using MediatR;

namespace ZonarHub.Application.Features.OrganizationSports.Get;

public sealed class GetOrganizationSportsHandler
    : IRequestHandler<GetOrganizationSportsQuery, Result<OrganizationSportsResponse>>
{
    private readonly IOrganizationRepository _organizations;
    private readonly ISportRepository _sports;
    private readonly IOrganizationSportRepository _orgSports;

    public GetOrganizationSportsHandler(
        IOrganizationRepository organizations,
        ISportRepository sports,
        IOrganizationSportRepository orgSports)
    {
        _organizations = organizations;
        _sports = sports;
        _orgSports = orgSports;
    }

    public async Task<Result<OrganizationSportsResponse>> Handle(
        GetOrganizationSportsQuery request,
        CancellationToken cancellationToken)
    {
        var orgId = new OrganizationId(request.OrganizationId);
        var org = await _organizations.GetByIdAsync(orgId, cancellationToken);
        if (org is null)
        {
            return Result.Failure<OrganizationSportsResponse>(OrganizationErrors.NotFound);
        }

        if (request.RequiredTenantId is { } tenantId && org.TenantId != tenantId)
        {
            return Result.Failure<OrganizationSportsResponse>(OrganizationErrors.CrossTenantAccessDenied);
        }

        var (allSports, _) = await _sports.ListAsync(
            new SportQuery(null, null, 1, int.MaxValue),
            cancellationToken);

        var enabledIds = await _orgSports.GetEnabledSportIdsAsync(orgId, cancellationToken);
        var enabledSet = new HashSet<SportId>(enabledIds);

        IReadOnlyList<OrganizationSportEntry> items = allSports
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new OrganizationSportEntry(
                s.Id.Value,
                s.Name,
                s.Key,
                s.Icon,
                s.IconSource.ToString().ToLowerInvariant(),
                s.SortOrder,
                s.IsActive && enabledSet.Contains(s.Id)))
            .ToList();

        return Result.Success(new OrganizationSportsResponse(items));
    }
}
