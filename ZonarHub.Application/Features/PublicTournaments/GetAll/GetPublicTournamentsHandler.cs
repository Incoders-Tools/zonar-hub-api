using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.PublicTournaments.GetAll;

/// <summary>
/// Lists the anonymous tournament showcase: public, active, non-cancelled tournaments of every organization,
/// ordered by start date and capped at <see cref="GetPublicTournamentsQuery.MaxLimit"/>.
/// </summary>
public sealed class GetPublicTournamentsHandler
    : IRequestHandler<GetPublicTournamentsQuery, Result<IReadOnlyList<PublicTournamentResponse>>>
{
    private readonly IPublicTournamentReadRepository _tournaments;
    private readonly PublicTournamentProjection _projection;

    public GetPublicTournamentsHandler(
        IPublicTournamentReadRepository tournaments,
        ICategoryRepository categories,
        IGenderRepository genders,
        ITournamentModalityRepository modalities,
        ITournamentStatusRepository statuses)
    {
        _tournaments = tournaments;
        _projection = new PublicTournamentProjection(categories, genders, modalities, statuses);
    }

    public async Task<Result<IReadOnlyList<PublicTournamentResponse>>> Handle(
        GetPublicTournamentsQuery request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit ?? GetPublicTournamentsQuery.MaxLimit, 1, GetPublicTournamentsQuery.MaxLimit);
        var records = await _tournaments.ListAsync(limit, cancellationToken);
        return Result.Success(await _projection.ProjectAsync(records, cancellationToken));
    }
}
