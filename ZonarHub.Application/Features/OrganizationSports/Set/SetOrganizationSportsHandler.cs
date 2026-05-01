using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;
using MediatR;

namespace ZonarHub.Application.Features.OrganizationSports.Set;

public sealed class SetOrganizationSportsHandler : IRequestHandler<SetOrganizationSportsCommand, Result>
{
    private readonly IOrganizationRepository _organizations;
    private readonly ISportRepository _sports;
    private readonly IOrganizationSportRepository _orgSports;
    private readonly IUnitOfWork _unitOfWork;

    public SetOrganizationSportsHandler(
        IOrganizationRepository organizations,
        ISportRepository sports,
        IOrganizationSportRepository orgSports,
        IUnitOfWork unitOfWork)
    {
        _organizations = organizations;
        _sports = sports;
        _orgSports = orgSports;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetOrganizationSportsCommand request, CancellationToken cancellationToken)
    {
        var orgId = new OrganizationId(request.OrganizationId);
        var org = await _organizations.GetByIdAsync(orgId, cancellationToken);
        if (org is null)
        {
            return Result.Failure(OrganizationErrors.NotFound);
        }

        if (request.RequiredTenantId is { } tenantId && org.TenantId != tenantId)
        {
            return Result.Failure(OrganizationErrors.CrossTenantAccessDenied);
        }

        // Verify all requested sport IDs actually exist
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

        await _orgSports.SetEnabledSportsAsync(orgId, requestedIds, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
