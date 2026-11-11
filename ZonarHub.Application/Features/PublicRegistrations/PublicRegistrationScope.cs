using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.PublicTournaments;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Features.PublicRegistrations;

/// <summary>
/// Resolves a slug with the public tournament page's visibility rule (the showcase read: public, active, with a slug and
/// not cancelled), then the tournament's full row for its registration rules. Malformed slugs are rejected without a
/// store query; every tournament outside the showcase is <see cref="PublicTournamentErrors.NotFound"/>.
/// </summary>
internal sealed class PublicRegistrationScope(IPublicTournamentReadRepository showcase, ITournamentAdminRepository tournaments)
{
    public async Task<Result<TournamentAdminDto>> LoadAsync(string slug, CancellationToken cancellationToken)
    {
        if (!PublicTournamentSlugFormat.IsWellFormed(slug))
            return Result.Failure<TournamentAdminDto>(PublicTournamentErrors.NotFound);

        var record = await showcase.GetBySlugAsync(slug, cancellationToken);
        var tournament = record is null ? null : await tournaments.GetByIdAsync(record.Id, cancellationToken);
        return tournament is null
            ? Result.Failure<TournamentAdminDto>(PublicTournamentErrors.NotFound)
            : Result.Success(tournament);
    }

    /// <summary>Where today (UTC) falls in the tournament's registration window (D2).</summary>
    public static RegistrationWindowState WindowState(TournamentAdminDto tournament, DateTime nowUtc) =>
        new RegistrationWindow(tournament.RegistrationStartDate, tournament.RegistrationEndDate).StateOn(DateOnly.FromDateTime(nowUtc));
}
