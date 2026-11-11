using System.Net;
using ZonarHub.Infrastructure.Persistence.Supabase;
using static ZonarHub.Tests.Infrastructure.Persistence.Supabase.SupabaseHttpRecorder;

namespace ZonarHub.Tests.Infrastructure.Persistence.Supabase;

/// <summary>
/// Dashboard registration count (D1): read from tournament_registrations (active registrations of the organization),
/// no longer from the legacy registrations table.
/// </summary>
public class RegistrationReadRepositoryTests
{
    private static readonly Guid Organization = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    [Fact]
    public async Task CountByOrganization_CountsActiveTournamentRegistrations_FromTheContentRangeTotal()
    {
        var http = new SupabaseHttpRecorder(_ => Counted("[{\"id\":\"x\"}]", "0-0/7"));

        var count = await new RegistrationReadRepository(http.Factory).CountByOrganizationAsync(Organization);

        var request = http.Single();
        Assert.Equal(("GET", "/rest/v1/tournament_registrations", "count=exact"), (request.Method, request.Path, request.Prefer));
        Assert.Equal(["select=id", $"organization_id=eq.{Organization}", "is_active=eq.true", "limit=1"], request.Query);
        Assert.Equal(7, count);
    }

    [Theory]
    [InlineData("*/0", 0)]
    [InlineData("*/25", 25)]
    [InlineData("0-0/*", 0)]
    [InlineData("0-0/", 0)]
    [InlineData("garbage", 0)]
    public async Task CountByOrganization_ReadsOnlyANumericTotal(string contentRange, int expected)
    {
        var http = new SupabaseHttpRecorder(_ => Counted("[]", contentRange));

        Assert.Equal(expected, await new RegistrationReadRepository(http.Factory).CountByOrganizationAsync(Organization));
    }

    [Fact]
    public async Task CountByOrganization_StoreFailureOrMissingHeader_IsZero()
    {
        var failure = new SupabaseHttpRecorder(_ => Problem(HttpStatusCode.InternalServerError, "XX000", "boom"));
        var missing = new SupabaseHttpRecorder(_ => Json("[]"));

        Assert.Equal(0, await new RegistrationReadRepository(failure.Factory).CountByOrganizationAsync(Organization));
        Assert.Equal(0, await new RegistrationReadRepository(missing.Factory).CountByOrganizationAsync(Organization));
    }
}
