using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Application.Features.AdminTournaments;

/// <summary>
/// Saves an admin tournament, assigning its public slug the first time it is saved public.
/// An existing slug is never regenerated: turning visibility off keeps it, turning it on again reuses it.
/// </summary>
public static class TournamentPublicSlugGeneration
{
    /// <summary>Total save attempts when a freshly generated slug collides with an existing one.</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Runs <paramref name="save"/>; when a new slug is generated and the store reports it as taken, retries
    /// with a fresh slug up to <see cref="MaxAttempts"/> saves, then lets the conflict propagate.
    /// </summary>
    internal static async Task<TournamentAdminDto> SaveAsync(
        TournamentAdminDto tournament,
        Func<TournamentAdminDto, Task<TournamentAdminDto>> save)
    {
        if (!tournament.IsPublic || tournament.PublicSlug is not null)
        {
            return await save(tournament);
        }

        for (var attempt = 1; ; attempt++)
        {
            var candidate = tournament with { PublicSlug = TournamentPublicSlug.Generate(tournament.Name) };
            try
            {
                return await save(candidate);
            }
            catch (TournamentPublicSlugConflictException) when (attempt < MaxAttempts)
            {
            }
        }
    }
}
