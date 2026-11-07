using System.Security.Cryptography;

namespace ZonarHub.Domain.Tournaments;

/// <summary>
/// Public share-link slug of a tournament: <c>&lt;slugified name&gt;-&lt;random base36 suffix&gt;</c>, lowercase
/// <c>[a-z0-9-]</c> only. Mirrors the <c>public_slug</c> check constraint in the database.
/// </summary>
public static class TournamentPublicSlug
{
    /// <summary>Maximum slug length accepted by the database check.</summary>
    public const int MaxLength = 80;

    /// <summary>Length of the random lowercase base36 suffix.</summary>
    public const int SuffixLength = 6;

    private const string Base36 = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const string FallbackName = "tournament";
    private const int NameMaxLength = MaxLength - SuffixLength - 1;

    /// <summary>
    /// Generates a new slug for the given tournament name. Every call returns a fresh random suffix.
    /// </summary>
    public static string Generate(string name)
    {
        var namePart = NamePart(name);
        return $"{namePart}-{RandomNumberGenerator.GetString(Base36, SuffixLength)}";
    }

    /// <summary>
    /// Reuses the tournament key slug, then turns its underscores into single hyphens and caps its length.
    /// </summary>
    private static string NamePart(string name)
    {
        var words = Tournament.Slugify(name ?? string.Empty)
            .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join('-', words);

        if (joined.Length > NameMaxLength)
        {
            joined = joined[..NameMaxLength].TrimEnd('-');
        }

        return joined.Length == 0 ? FallbackName : joined;
    }
}
