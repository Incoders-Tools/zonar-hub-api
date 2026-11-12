using ZonarHub.Infrastructure.Persistence.Supabase;

namespace ZonarHub.Tests.Infrastructure.Persistence.Supabase;

public sealed class SupabaseQueryTests
{
    [Fact]
    public void Value_UrlEncodesReservedCharacters()
    {
        var value = SupabaseQuery.Value(" admin@example.com&role=eq.system_admin ");

        Assert.Equal("admin%40example.com%26role%3Deq.system_admin", value);
    }

    [Fact]
    public void ContainsPattern_RemovesPostgrestWildcardsBeforeEncoding()
    {
        var value = SupabaseQuery.ContainsPattern(" *admin% ");

        Assert.Equal("admin", value);
    }

    [Fact]
    public void QuotedContainsPattern_DropsLogicTreeSeparatorsAndQuotes_ThenEncodes()
    {
        Assert.Equal("ana%20p%40x.com", SupabaseQuery.QuotedContainsPattern(" ana, (p)@x.com\\\" *% "));
    }

    [Fact]
    public void QuotedContainsPattern_ReturnsNullWhenNothingSearchableRemains()
    {
        Assert.Null(SupabaseQuery.QuotedContainsPattern(" ,()\"\\*% "));
        Assert.Null(SupabaseQuery.QuotedContainsPattern(null));
    }

    [Fact]
    public void ContainsPattern_ReturnsNullWhenOnlyWildcardsRemain()
    {
        var value = SupabaseQuery.ContainsPattern(" *%% ");

        Assert.Null(value);
    }
}
