using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.Update;

public sealed class UpdateAdminRoleHandler : IRequestHandler<UpdateAdminRoleCommand, Result<AdminRoleResponse>>
{
    private readonly IRoleRepository _roles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateAdminRoleHandler(
        IRoleRepository roles,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _roles = roles;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<AdminRoleResponse>> Handle(
        UpdateAdminRoleCommand request,
        CancellationToken cancellationToken)
    {
        var current = await _roles.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure<AdminRoleResponse>(AdminRoleErrors.NotFound);
        }

        if (current.IsSystem)
        {
            return Result.Failure<AdminRoleResponse>(AdminRoleErrors.ImmutableSystemRole);
        }

        var nextName = current.Name;
        if (request.Name is not null)
        {
            var normalizedName = AdminRoleName.Normalize(request.Name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return Result.Failure<AdminRoleResponse>(AdminRoleErrors.NameRequired);
            }

            if (!AdminRoleName.IsValid(normalizedName))
            {
                return Result.Failure<AdminRoleResponse>(AdminRoleErrors.NameInvalid);
            }

            var duplicate = await _roles.GetByNameAsync(normalizedName, cancellationToken);
            if (duplicate is not null && !string.Equals(duplicate.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<AdminRoleResponse>(AdminRoleErrors.NameAlreadyExists);
            }

            nextName = normalizedName;
        }

        var nextDescription = request.Description is null ? current.Description : request.Description.Trim();
        if (string.IsNullOrWhiteSpace(nextDescription))
        {
            return Result.Failure<AdminRoleResponse>(AdminRoleErrors.DescriptionRequired);
        }

        var updated = current with
        {
            Name = nextName,
            Description = nextDescription,
            IsActive = request.IsActive ?? current.IsActive,
            UpdatedAtUtc = _clock.UtcNow
        };

        _roles.Update(updated);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(updated.ToResponse());
    }
}
