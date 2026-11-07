using System.Text.RegularExpressions;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Tests.Domain.Tournaments;

public class TournamentPublicSlugTests
{
    private static readonly Regex Format = new("^[a-z0-9]+(-[a-z0-9]+)*-[a-z0-9]{6}$");

    [Fact]
    public void Generate_UsesSlugifiedNameAndSixCharBase36Suffix()
    {
        var slug = TournamentPublicSlug.Generate("Open Cup 2026");

        Assert.Matches(Format, slug);
        Assert.StartsWith("open-cup-2026-", slug);
        Assert.Equal("open-cup-2026-".Length + TournamentPublicSlug.SuffixLength, slug.Length);
    }

    [Fact]
    public void Generate_StripsAccentsUnderscoresAndSymbols()
    {
        var slug = TournamentPublicSlug.Generate("  Torneo   Ñandú__Pádel & Co!  ");

        Assert.Matches(Format, slug);
        Assert.StartsWith("torneo-nandu-padel-co-", slug);
    }

    [Fact]
    public void Generate_NameWithoutSlugCharacters_FallsBackToTournament()
    {
        var slug = TournamentPublicSlug.Generate("¡¿#$%?!");

        Assert.Matches(Format, slug);
        Assert.StartsWith("tournament-", slug);
    }

    [Fact]
    public void Generate_LongName_IsCappedToTheMaxLengthWithoutTrailingHyphen()
    {
        var slug = TournamentPublicSlug.Generate(string.Join(' ', Enumerable.Repeat("championship", 20)));

        Assert.True(slug.Length <= TournamentPublicSlug.MaxLength, $"length {slug.Length}");
        Assert.Matches(Format, slug);
    }

    [Fact]
    public void Generate_ProducesDifferentSuffixesAcrossCalls()
    {
        var slugs = Enumerable.Range(0, 20).Select(_ => TournamentPublicSlug.Generate("Open")).ToHashSet();

        Assert.True(slugs.Count > 1);
    }
}
