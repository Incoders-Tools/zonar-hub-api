using System.Text.RegularExpressions;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Application.Features.PublicTournaments;

/// <summary>
/// Shape of a stored public slug, mirroring the <c>tournaments.public_slug</c> database check
/// (3 to <see cref="TournamentPublicSlug.MaxLength"/> characters, lowercase alphanumeric words joined by single hyphens).
/// Anything else cannot match a tournament, so it is rejected before reaching the store.
/// </summary>
internal static partial class PublicTournamentSlugFormat
{
    private const int MinLength = 3;

    public static bool IsWellFormed(string? slug) =>
        slug is not null &&
        slug.Length >= MinLength &&
        slug.Length <= TournamentPublicSlug.MaxLength &&
        SlugPattern().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
