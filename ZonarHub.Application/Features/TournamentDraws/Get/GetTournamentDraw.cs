using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentDraws.Get;

/// <summary>The tournament's stored draw resolved with its results (admin shape).</summary>
public sealed record GetTournamentDrawQuery(Guid TournamentId) : IRequest<Result<TournamentDrawResponse>>;

public sealed class GetTournamentDrawHandler
    : IRequestHandler<GetTournamentDrawQuery, Result<TournamentDrawResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentDrawRepository _draws;

    public GetTournamentDrawHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentDrawRepository draws)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _draws = draws;
    }

    public async Task<Result<TournamentDrawResponse>> Handle(
        GetTournamentDrawQuery request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentDrawResponse>(tournament.Error);

        var snapshot = await _draws.GetByTournamentIdAsync(request.TournamentId, cancellationToken);
        if (snapshot is null)
            return Result.Failure<TournamentDrawResponse>(TournamentDrawErrors.NotFound);

        return Result.Success(TournamentDrawScope.ToAdminResponse(
            snapshot.Draw, snapshot.Results));
    }
}
