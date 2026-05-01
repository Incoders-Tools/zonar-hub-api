using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tenants;
using MediatR;

namespace ZonarHub.Application.Features.TenantSports.Get;

public sealed class GetTenantSportsHandler
    : IRequestHandler<GetTenantSportsQuery, Result<TenantSportsResponse>>
{
    private readonly ISportRepository _sports;
    private readonly ITenantSportRepository _tenantSports;

    public GetTenantSportsHandler(
        ISportRepository sports,
        ITenantSportRepository tenantSports)
    {
        _sports = sports;
        _tenantSports = tenantSports;
    }

    public async Task<Result<TenantSportsResponse>> Handle(
        GetTenantSportsQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = new TenantId(request.TenantId);
        if (request.RequiredTenantId is { } requiredTenantId && request.TenantId != requiredTenantId)
        {
            return Result.Failure<TenantSportsResponse>(TenantErrors.NotFound);
        }

        var (allSports, _) = await _sports.ListAsync(
            new SportQuery(null, null, 1, int.MaxValue),
            cancellationToken);

        var enabledIds = await _tenantSports.GetEnabledSportIdsAsync(tenantId, cancellationToken);
        var enabledSet = new HashSet<SportId>(enabledIds);

        IReadOnlyList<TenantSportEntry> items = allSports
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new TenantSportEntry(
                s.Id.Value,
                s.Name,
                s.Key,
                s.Icon,
                s.IconSource.ToString().ToLowerInvariant(),
                s.SortOrder,
                s.IsActive && enabledSet.Contains(s.Id)))
            .ToList();

        return Result.Success(new TenantSportsResponse(items));
    }
}
