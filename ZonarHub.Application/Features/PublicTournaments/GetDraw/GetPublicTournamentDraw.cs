using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.PublicTournaments.GetDraw;

/// <summary>The published draw of a showcase tournament, resolved with its results (public D7 shape).</summary>
public sealed record GetPublicTournamentDrawQuery(string Slug) : IRequest<Result<TournamentDrawResponse>>;

/// <summary>
/// Resolves the tournament through the showcase read (same filters as the public detail), then its published draw.
/// Malformed slugs are rejected without querying the store; a tournament outside the showcase is
/// <see cref="PublicTournamentErrors.NotFound"/>, one without a published draw <see cref="PublicTournamentErrors.DrawNotFound"/>.
/// </summary>
public sealed class GetPublicTournamentDrawHandler
    : IRequestHandler<GetPublicTournamentDrawQuery, Result<TournamentDrawResponse>>
{
    private readonly IPublicTournamentReadRepository _tournaments;
    private readonly ITournamentDrawRepository _draws;

    public GetPublicTournamentDrawHandler(IPublicTournamentReadRepository tournaments, ITournamentDrawRepository draws)
    {
        _tournaments = tournaments;
        _draws = draws;
    }

    public async Task<Result<TournamentDrawResponse>> Handle(
        GetPublicTournamentDrawQuery request,
        CancellationToken cancellationToken)
    {
        if (!PublicTournamentSlugFormat.IsWellFormed(request.Slug))
            return Result.Failure<TournamentDrawResponse>(PublicTournamentErrors.NotFound);

        var tournament = await _tournaments.GetBySlugAsync(request.Slug, cancellationToken);
        if (tournament is null)
            return Result.Failure<TournamentDrawResponse>(PublicTournamentErrors.NotFound);

        var published = await _draws.GetPublishedByTournamentIdAsync(tournament.Id, cancellationToken);
        if (published is null)
            return Result.Failure<TournamentDrawResponse>(PublicTournamentErrors.DrawNotFound);

        var results = TournamentDrawScope.CurrentResults(published.Structure, published.Results);
        return Result.Success(TournamentDrawResponseMapper.ToResponse(
            TournamentDrawScope.Resolve(published.Structure, results, published.Scoring),
            TournamentDrawStatus.Published,
            published.PublishedAtUtc,
            published.Options,
            TournamentDrawAudience.Public));
    }
}
