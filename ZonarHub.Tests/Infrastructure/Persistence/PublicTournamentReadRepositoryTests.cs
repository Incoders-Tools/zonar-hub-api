using System.Net;
using System.Text;
using ZonarHub.Infrastructure.Persistence.Supabase;

namespace ZonarHub.Tests.Infrastructure.Persistence;

/// <summary>
/// The showcase filter runs in PostgREST: the repository must ask only for public, active, non-cancelled tournaments
/// with a slug, ordered by start date, and must never select admin-only columns.
/// </summary>
public class PublicTournamentReadRepositoryTests
{
    private static readonly string[] ShowcaseFilters =
    [
        "is_public=eq.true",
        "is_active=eq.true",
        "public_slug=not.is.null",
        "or=(status.is.null,status.neq.cancelled)",
    ];

    private static readonly string[] ForbiddenColumns =
    [
        "*", "id", "organization_id", "tenant_id", "complex_id", "selected_court_ids", "observations", "sum_value",
        "points_to_award", "created_by_user_id", "legal_name", "tournament_type_id", "rule_set_id",
    ];

    [Fact]
    public async Task List_FiltersTheShowcaseInTheStore_OrderedByStartDate_AndLimited()
    {
        var urls = new List<string>();
        var repository = CreateRepository(urls, _ => Json("[]"));

        var items = await repository.ListAsync(25);

        Assert.Empty(items);
        var query = Query(Assert.Single(urls));
        Assert.Equal("/rest/v1/tournaments", Path(urls[0]));
        foreach (var filter in ShowcaseFilters)
        {
            Assert.Contains(filter, query);
        }

        Assert.Contains("order=start_date.asc,name.asc", query);
        Assert.Contains("limit=25", query);
        AssertSelectHasNoForbiddenColumns(query);
    }

    [Fact]
    public async Task List_MapsTheEmbeddedSportComplexAndOrganization()
    {
        var repository = CreateRepository([], _ => Json($"[{RowJson()}]"));

        var item = Assert.Single(await repository.ListAsync(100));

        Assert.Equal("open-norte-abc123", item.Slug);
        Assert.Equal("Open Norte", item.Name);
        Assert.Equal("Summer open", item.Description);
        Assert.Equal("Best of three", item.Rules);
        Assert.Equal(new DateOnly(2026, 11, 20), item.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 22), item.EndDate);
        Assert.Equal(new DateOnly(2026, 11, 1), item.RegistrationStartDate);
        Assert.Equal(new DateOnly(2026, 11, 15), item.RegistrationEndDate);
        Assert.Equal("registration_open", item.Status);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), item.CategoryId);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), item.GenderId);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-0000000000b1"), item.ModalityId);
        Assert.Equal(32, item.MaxPairs);
        Assert.Equal("https://cdn.example/t.png", item.ImageUrl);
        Assert.Equal("https://cdn.example/cover.png", item.CoverImageUrl);
        Assert.Equal(25000m, item.RegistrationFeePerPair);
        Assert.Equal(500000m, item.PrizeMoney);
        Assert.Equal(new ZonarHub.Application.Abstractions.PublicSportRecord("padel", "Pádel", "padel.svg", "svg"), item.Sport);
        Assert.Equal(new ZonarHub.Application.Abstractions.PublicComplexRecord("Complejo Norte", "Av. 742"), item.Complex);

        var organization = item.Organization!;
        Assert.Equal("Club Norte", organization.DisplayName);
        Assert.Equal("https://cdn.example/logo.png", organization.LogoUrl);
        Assert.Equal("Padel club", organization.Description);
        Assert.Equal("circuito", organization.Type);
        Assert.True(organization.IsActive);
        Assert.True(organization.Profile.Enabled);
        Assert.Equal("https://club.example", organization.Profile.WebsiteUrl);
        Assert.Equal("https://instagram.com/club", organization.Profile.InstagramUrl);
        Assert.Null(organization.Profile.FacebookUrl);
        Assert.Equal("https://tiktok.com/@club", organization.Profile.TiktokUrl);
        Assert.Equal("https://youtube.com/@club", organization.Profile.YoutubeUrl);
        Assert.Equal("+5491122334455", organization.Profile.WhatsappPhone);
        Assert.Equal("hola@club.example", organization.Profile.ContactEmail);
    }

    [Fact]
    public async Task List_RowWithoutEmbeds_MapsThemToNull()
    {
        const string row = """{"public_slug":"open-abc123","name":"Open","start_date":"2026-11-20","end_date":"2026-11-21","sport":null,"complex":null,"organization":null}""";
        var repository = CreateRepository([], _ => Json($"[{row}]"));

        var item = Assert.Single(await repository.ListAsync(10));

        Assert.Null(item.Sport);
        Assert.Null(item.Complex);
        Assert.Null(item.Organization);
        Assert.Null(item.Status);
    }

    [Fact]
    public async Task BySlug_FiltersTheSlugAndTheShowcaseInTheStore()
    {
        var urls = new List<string>();
        var repository = CreateRepository(urls, _ => Json($"[{RowJson()}]"));

        var item = await repository.GetBySlugAsync("open-norte-abc123");

        Assert.Equal("open-norte-abc123", item!.Slug);
        var query = Query(Assert.Single(urls));
        Assert.Contains("public_slug=eq.open-norte-abc123", query);
        foreach (var filter in ShowcaseFilters)
        {
            Assert.Contains(filter, query);
        }

        Assert.Contains("limit=1", query);
        AssertSelectHasNoForbiddenColumns(query);
    }

    [Fact]
    public async Task BySlug_NoMatchingRow_ReturnsNull()
    {
        var repository = CreateRepository([], _ => Json("[]"));

        Assert.Null(await repository.GetBySlugAsync("hidden-cup-abc123"));
    }

    [Fact]
    public async Task StoreFailure_Throws()
    {
        var repository = CreateRepository([], _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"message":"column tournaments.is_public does not exist"}"""),
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => repository.ListAsync(10));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.GetBySlugAsync("open-abc123"));
    }

    private static void AssertSelectHasNoForbiddenColumns(string query)
    {
        var select = query.Split('&').Single(p => p.StartsWith("select=", StringComparison.Ordinal))["select=".Length..];
        var columns = select
            .Split([',', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Contains(':') ? c[(c.IndexOf(':') + 1)..] : c)
            .ToList();
        foreach (var column in ForbiddenColumns)
        {
            Assert.DoesNotContain(column, columns);
        }
    }

    private static string Path(string url) => new Uri(url).AbsolutePath;

    private static string Query(string url) => Uri.UnescapeDataString(new Uri(url).Query.TrimStart('?'));

    private static string RowJson() => """
        {
          "public_slug": "open-norte-abc123",
          "name": "Open Norte",
          "description": "Summer open",
          "rules": "Best of three",
          "start_date": "2026-11-20",
          "end_date": "2026-11-22",
          "registration_start_date": "2026-11-01",
          "registration_end_date": "2026-11-15",
          "status": "registration_open",
          "category_id": "00000000-0000-0000-0000-0000000000c1",
          "gender_id": "00000000-0000-0000-0000-0000000000a1",
          "modality_id": "00000000-0000-0000-0000-0000000000b1",
          "max_pairs": 32,
          "image_url": "https://cdn.example/t.png",
          "cover_image_url": "https://cdn.example/cover.png",
          "registration_fee_per_pair": 25000.00,
          "prize_money": 500000.00,
          "sport": { "key": "padel", "name": "Pádel", "icon": "padel.svg", "icon_source": "svg" },
          "complex": { "name": "Complejo Norte", "address": "Av. 742" },
          "organization": {
            "display_name": "Club Norte",
            "logo_url": "https://cdn.example/logo.png",
            "description": "Padel club",
            "type": "circuito",
            "is_active": true,
            "public_profile_enabled": true,
            "website_url": "https://club.example",
            "instagram_url": "https://instagram.com/club",
            "facebook_url": " ",
            "tiktok_url": "https://tiktok.com/@club",
            "youtube_url": "https://youtube.com/@club",
            "whatsapp_phone": "+5491122334455",
            "contact_email": "hola@club.example"
          }
        }
        """;

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static PublicTournamentReadRepository CreateRepository(
        List<string> urls,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        return new PublicTournamentReadRepository(new ClientFactory(client));
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
