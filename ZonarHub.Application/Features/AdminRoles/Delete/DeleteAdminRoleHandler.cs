using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.Delete;

public sealed class DeleteAdminRoleHandler : IRequestHandler<DeleteAdminRoleCommand, Result>
{
    private readonly IRoleRepository _roles;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteAdminRoleHandler(
        IRoleRepository roles,
        IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteAdminRoleCommand request, CancellationToken cancellationToken)
    {
        var current = await _roles.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure(AdminRoleErrors.NotFound);
        }

        if (current.IsSystem)
        {
            return Result.Failure(AdminRoleErrors.ImmutableSystemRole);
        }

        await _roles.RemoveAsync(request.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
