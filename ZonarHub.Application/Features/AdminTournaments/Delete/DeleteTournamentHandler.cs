using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminTournaments.Delete;

public sealed class DeleteTournamentHandler : IRequestHandler<DeleteTournamentCommand, Result>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly ITournamentAdminRepository _tournaments;

    public DeleteTournamentHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _tournaments = tournaments;
    }

    public async Task<Result> Handle(DeleteTournamentCommand request, CancellationToken cancellationToken)
    {
        var current = await _tournaments.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure(AdminTournamentErrors.NotFound);
        }

        if (!await _access.CanAccessAsync(current.OrganizationId, cancellationToken))
        {
            return Result.Failure(AdminTournamentErrors.Forbidden);
        }

        await _tournaments.DeleteAsync(request.Id, cancellationToken);
        return Result.Success();
    }
}
