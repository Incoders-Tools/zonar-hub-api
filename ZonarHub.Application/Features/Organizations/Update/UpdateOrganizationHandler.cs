using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.Update;

public sealed class UpdateOrganizationHandler
    : IRequestHandler<UpdateOrganizationCommand, Result<OrganizationResponse>>
{
    private readonly IOrganizationRepository _organizations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateOrganizationHandler(
        IOrganizationRepository organizations,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _organizations = organizations;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OrganizationResponse>> Handle(
        UpdateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var org = await _organizations.GetByIdAsync(new OrganizationId(request.Id), cancellationToken);
        if (org is null)
        {
            return Result.Failure<OrganizationResponse>(OrganizationErrors.NotFound);
        }

        if (request.RequiredTenantId is { } tenantId && org.TenantId != tenantId)
        {
            return Result.Failure<OrganizationResponse>(OrganizationErrors.CrossTenantAccessDenied);
        }

        var updated = org.Update(
            request.DisplayName,
            request.LegalName,
            request.Description,
            request.Type,
            request.LogoUrl,
            request.IsActive,
            _clock.UtcNow);

        if (updated.IsFailure)
        {
            return Result.Failure<OrganizationResponse>(updated.Error);
        }

        _organizations.Update(org);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(OrganizationResponse.FromDomain(org));
    }
}
