using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers.Delete;

public sealed class DeleteAdminUserHandler : IRequestHandler<DeleteAdminUserCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteAdminUserHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _users = users;
        _assignments = assignments;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteAdminUserCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return Result.Failure(AdminUserErrors.CallerNotAuthenticated);
        }

        if (_currentUser.UserId.Value == request.UserId)
        {
            return Result.Failure(AdminUserErrors.CannotDeleteSelf);
        }

        var caller = await _users.GetByIdAsync(new UserId(_currentUser.UserId.Value), cancellationToken);
        if (caller is null)
        {
            return Result.Failure(AdminUserErrors.CallerNotFound);
        }

        var user = await _users.GetByIdAsync(new UserId(request.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(AdminUserErrors.UserNotFound);
        }

        if (!AdminUserAuthorization.CanManageUser(caller, user))
        {
            return Result.Failure(AdminUserErrors.Forbidden);
        }

        await _assignments.RemoveByUserIdAsync(user.Id.Value, cancellationToken);
        await _users.RemoveAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
