using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.Create;

public sealed class CreateAdminRoleHandler : IRequestHandler<CreateAdminRoleCommand, Result<AdminRoleResponse>>
{
    private readonly IRoleRepository _roles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateAdminRoleHandler(
        IRoleRepository roles,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _roles = roles;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<AdminRoleResponse>> Handle(
        CreateAdminRoleCommand request,
        CancellationToken cancellationToken)
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

        var description = request.Description.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure<AdminRoleResponse>(AdminRoleErrors.DescriptionRequired);
        }

        var existing = await _roles.GetByNameAsync(normalizedName, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<AdminRoleResponse>(AdminRoleErrors.NameAlreadyExists);
        }

        var nowUtc = _clock.UtcNow;
        var role = new RoleDefinition(
            $"role_{Guid.NewGuid():N}",
            normalizedName,
            description,
            request.IsActive,
            IsSystem: false,
            CreatedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc);

        await _roles.AddAsync(role, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(role.ToResponse());
    }
}
