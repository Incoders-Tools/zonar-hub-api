using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.Create;

public sealed class CreateOrganizationHandler
    : IRequestHandler<CreateOrganizationCommand, Result<OrganizationResponse>>
{
    private readonly IOrganizationRepository _organizations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateOrganizationHandler(
        IOrganizationRepository organizations,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _organizations = organizations;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OrganizationResponse>> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _organizations.GetByTenantAndDisplayNameAsync(
            request.TenantId,
            request.DisplayName,
            cancellationToken);

        if (existing is not null)
        {
            return Result.Failure<OrganizationResponse>(OrganizationErrors.DuplicateDisplayName);
        }

        var created = Organization.Create(
            OrganizationId.New(),
            request.TenantId,
            request.DisplayName,
            request.LegalName,
            request.Description,
            request.Type,
            request.LogoUrl,
            request.CreatedByUserId,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<OrganizationResponse>(created.Error);
        }

        await _organizations.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(OrganizationResponse.FromDomain(created.Value));
    }
}
