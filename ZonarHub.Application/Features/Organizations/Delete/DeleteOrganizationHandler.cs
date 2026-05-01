using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.Delete;

public sealed class DeleteOrganizationHandler : IRequestHandler<DeleteOrganizationCommand, Result>
{
    private readonly IOrganizationRepository _organizations;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteOrganizationHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork)
    {
        _organizations = organizations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteOrganizationCommand request, CancellationToken cancellationToken)
    {
        var org = await _organizations.GetByIdAsync(new OrganizationId(request.Id), cancellationToken);
        if (org is null)
        {
            return Result.Failure(OrganizationErrors.NotFound);
        }

        if (request.RequiredTenantId is { } tenantId && org.TenantId != tenantId)
        {
            return Result.Failure(OrganizationErrors.CrossTenantAccessDenied);
        }

        _organizations.Remove(org);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
