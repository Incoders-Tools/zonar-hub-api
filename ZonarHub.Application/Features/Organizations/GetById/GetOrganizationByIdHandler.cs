using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.GetById;

public sealed class GetOrganizationByIdHandler
    : IRequestHandler<GetOrganizationByIdQuery, Result<OrganizationResponse>>
{
    private readonly IOrganizationRepository _organizations;

    public GetOrganizationByIdHandler(IOrganizationRepository organizations)
    {
        _organizations = organizations;
    }

    public async Task<Result<OrganizationResponse>> Handle(
        GetOrganizationByIdQuery request,
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

        return Result.Success(OrganizationResponse.FromDomain(org));
    }
}
