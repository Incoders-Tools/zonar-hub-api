using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentDraws.Courts;

/// <summary>Lists the courts a result of the tournament's draw can be recorded on (D13).</summary>
public sealed record ListTournamentDrawCourtsQuery(Guid TournamentId)
    : IRequest<Result<IReadOnlyList<TournamentDrawCourtResponse>>>;

/// <summary>A court a match of the draw can be played on.</summary>
/// <param name="Id">Court id to send as <c>courtId</c> in a match result.</param>
/// <param name="Name">Current court name.</param>
public sealed record TournamentDrawCourtResponse(Guid Id, string Name);

/// <remarks>
/// Applies the tournament tenant rule, then returns the eligible courts of <see cref="TournamentDrawCourts"/> (empty when
/// the tournament has no complex of its own). The draw itself does not need to exist.
/// </remarks>
public sealed class ListTournamentDrawCourtsHandler
    : IRequestHandler<ListTournamentDrawCourtsQuery, Result<IReadOnlyList<TournamentDrawCourtResponse>>>
{
    private readonly TournamentDrawScope _scope;
    private readonly TournamentDrawCourts _courts;

    public ListTournamentDrawCourtsHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ICourtRepository courts,
        IComplexRepository complexes)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _courts = new TournamentDrawCourts(courts, complexes);
    }

    public async Task<Result<IReadOnlyList<TournamentDrawCourtResponse>>> Handle(
        ListTournamentDrawCourtsQuery request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<IReadOnlyList<TournamentDrawCourtResponse>>(tournament.Error);

        var courts = await _courts.ListAsync(tournament.Value, cancellationToken);
        return Result.Success<IReadOnlyList<TournamentDrawCourtResponse>>(
            courts.Select(court => new TournamentDrawCourtResponse(court.Id.Value, court.Name)).ToList());
    }
}
