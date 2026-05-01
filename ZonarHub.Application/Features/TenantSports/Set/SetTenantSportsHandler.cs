using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tenants;
using MediatR;

namespace ZonarHub.Application.Features.TenantSports.Set;

public sealed class SetTenantSportsHandler : IRequestHandler<SetTenantSportsCommand, Result>
{
    private readonly ISportRepository _sports;
    private readonly ITenantSportRepository _tenantSports;
    private readonly IUnitOfWork _unitOfWork;

    public SetTenantSportsHandler(
        ISportRepository sports,
        ITenantSportRepository tenantSports,
        IUnitOfWork unitOfWork)
    {
        _sports = sports;
        _tenantSports = tenantSports;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetTenantSportsCommand request, CancellationToken cancellationToken)
    {
        var tenantId = new TenantId(request.TenantId);
        if (request.RequiredTenantId is { } requiredTenantId && request.TenantId != requiredTenantId)
        {
            return Result.Failure(TenantErrors.NotFound);
        }

        var requestedIds = request.EnabledSportIds
            .Distinct()
            .Select(id => new SportId(id))
            .ToList();

        if (requestedIds.Count > 0)
        {
            var found = await _sports.GetByIdsAsync(requestedIds, cancellationToken);
            if (found.Count != requestedIds.Count)
            {
                return Result.Failure(SportErrors.NotFound);
            }
        }

        await _tenantSports.SetEnabledSportsAsync(tenantId, requestedIds, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
