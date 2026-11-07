using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.PublicTournaments.GetBySlug;

/// <summary>
/// Resolves one showcase tournament by its public slug. Malformed slugs are rejected without querying the store;
/// unknown, non-public, inactive and cancelled tournaments all return <see cref="PublicTournamentErrors.NotFound"/>.
/// </summary>
public sealed class GetPublicTournamentBySlugHandler
    : IRequestHandler<GetPublicTournamentBySlugQuery, Result<PublicTournamentResponse>>
{
    private readonly IPublicTournamentReadRepository _tournaments;
    private readonly PublicTournamentProjection _projection;

    public GetPublicTournamentBySlugHandler(
        IPublicTournamentReadRepository tournaments,
        ICategoryRepository categories,
        IGenderRepository genders,
        ITournamentModalityRepository modalities,
        ITournamentStatusRepository statuses)
    {
        _tournaments = tournaments;
        _projection = new PublicTournamentProjection(categories, genders, modalities, statuses);
    }

    public async Task<Result<PublicTournamentResponse>> Handle(
        GetPublicTournamentBySlugQuery request,
        CancellationToken cancellationToken)
    {
        if (!PublicTournamentSlugFormat.IsWellFormed(request.Slug))
        {
            return Result.Failure<PublicTournamentResponse>(PublicTournamentErrors.NotFound);
        }

        var record = await _tournaments.GetBySlugAsync(request.Slug, cancellationToken);
        if (record is null)
        {
            return Result.Failure<PublicTournamentResponse>(PublicTournamentErrors.NotFound);
        }

        var projected = await _projection.ProjectAsync([record], cancellationToken);
        return Result.Success(projected[0]);
    }
}
