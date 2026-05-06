using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.Create;

public sealed class CreateOrganizationHandler
    : IRequestHandler<CreateOrganizationCommand, Result<OrganizationResponse>>
{
    private readonly IOrganizationRepository _organizations;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateOrganizationHandler(
        IOrganizationRepository organizations,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _organizations = organizations;
        _users = users;
        _assignments = assignments;
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

        var creator = await _users.GetByIdAsync(new ZonarHub.Domain.Users.UserId(request.CreatedByUserId), cancellationToken);
        if (creator is not null)
        {
            var assignmentIds = (await _assignments.GetOrganizationIdsByUserIdAsync(creator.Id.Value, cancellationToken))
                .Where(id => id != Guid.Empty)
                .ToList();

            if (creator.OrganizationId is { } currentPrimary && !assignmentIds.Contains(currentPrimary))
            {
                assignmentIds.Insert(0, currentPrimary);
            }

            if (!assignmentIds.Contains(created.Value.Id.Value))
            {
                assignmentIds.Add(created.Value.Id.Value);
            }

            await _assignments.SetOrganizationIdsAsync(creator.Id.Value, assignmentIds, cancellationToken);

            if (creator.OrganizationId is null)
            {
                var assignPrimary = creator.AssignOrganization(created.Value.Id.Value, _clock.UtcNow);
                if (assignPrimary.IsFailure)
                {
                    return Result.Failure<OrganizationResponse>(assignPrimary.Error);
                }

                _users.Update(creator);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(OrganizationResponse.FromDomain(created.Value));
    }
}
