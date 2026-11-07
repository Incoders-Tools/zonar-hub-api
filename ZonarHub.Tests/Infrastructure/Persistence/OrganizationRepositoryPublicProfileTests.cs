using System.Net;
using System.Text;
using System.Text.Json;
using ZonarHub.Domain.Organizations;
using ZonarHub.Infrastructure.Persistence.Supabase;

namespace ZonarHub.Tests.Infrastructure.Persistence;

public class OrganizationRepositoryPublicProfileTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] ProfileColumns =
    [
        "public_profile_enabled", "website_url", "instagram_url", "facebook_url",
        "tiktok_url", "youtube_url", "whatsapp_phone", "contact_email",
    ];

    [Fact]
    public async Task Add_SendsConsentAndLinkColumns()
    {
        var requests = new List<(string Method, string? Body)>();
        var (repository, ops, http) = CreateRepository(requests, _ => Json("[]"));

        await repository.AddAsync(Organization(Profile()));
        await ops.FlushAsync(http, CancellationToken.None);

        var request = Assert.Single(requests);
        Assert.Equal("POST", request.Method);
        AssertProfileColumns(request.Body!);
    }

    [Fact]
    public async Task Update_SendsConsentAndLinkColumns()
    {
        var requests = new List<(string Method, string? Body)>();
        var (repository, ops, http) = CreateRepository(requests, _ => Json("[]"));

        repository.Update(Organization(Profile()));
        await ops.FlushAsync(http, CancellationToken.None);

        var request = Assert.Single(requests);
        Assert.Equal("PATCH", request.Method);
        AssertProfileColumns(request.Body!);
    }

    [Fact]
    public async Task GetById_MapsConsentAndLinkColumns()
    {
        var id = Guid.NewGuid();
        var (repository, _, _) = CreateRepository([], _ => Json(RowJson(id, withProfile: true)));

        var organization = await repository.GetByIdAsync(new OrganizationId(id));

        var profile = organization!.PublicProfile;
        Assert.True(profile.Enabled);
        Assert.Equal("https://club.example", profile.WebsiteUrl);
        Assert.Equal("https://instagram.com/club", profile.InstagramUrl);
        Assert.Equal("https://facebook.com/club", profile.FacebookUrl);
        Assert.Equal("https://tiktok.com/@club", profile.TiktokUrl);
        Assert.Equal("https://youtube.com/@club", profile.YoutubeUrl);
        Assert.Equal("+5491122334455", profile.WhatsappPhone);
        Assert.Equal("hola@club.example", profile.ContactEmail);
    }

    [Fact]
    public async Task GetById_RowWithoutProfileColumns_MapsToAnEmptyProfile()
    {
        var id = Guid.NewGuid();
        var (repository, _, _) = CreateRepository([], _ => Json(RowJson(id, withProfile: false)));

        var organization = await repository.GetByIdAsync(new OrganizationId(id));

        Assert.Equal("Club", organization!.DisplayName);
        Assert.Equal(OrganizationPublicProfile.Empty, organization.PublicProfile);
    }

    private static void AssertProfileColumns(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        foreach (var column in ProfileColumns)
        {
            Assert.True(root.TryGetProperty(column, out _), column);
        }

        Assert.True(root.GetProperty("public_profile_enabled").GetBoolean());
        Assert.Equal("https://club.example", root.GetProperty("website_url").GetString());
        Assert.Equal("https://instagram.com/club", root.GetProperty("instagram_url").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("facebook_url").ValueKind);
        Assert.Equal("+5491122334455", root.GetProperty("whatsapp_phone").GetString());
        Assert.Equal("hola@club.example", root.GetProperty("contact_email").GetString());
    }

    private static OrganizationPublicProfile Profile() => OrganizationPublicProfile.Create(
        true, "https://club.example", "https://instagram.com/club", null, null, null, "+5491122334455", "hola@club.example");

    private static Organization Organization(OrganizationPublicProfile profile) =>
        ZonarHub.Domain.Organizations.Organization.Create(
            OrganizationId.New(), Guid.NewGuid(), "Club", null, null, OrganizationType.Estandar, null, Guid.NewGuid(), Now, profile).Value;

    private static string RowJson(Guid id, bool withProfile)
    {
        var row = new Dictionary<string, object?>
        {
            ["id"] = id.ToString(),
            ["tenant_id"] = Guid.NewGuid().ToString(),
            ["display_name"] = "Club",
            ["legal_name"] = null,
            ["description"] = null,
            ["type"] = "estandar",
            ["logo_url"] = null,
            ["is_active"] = true,
            ["created_by_user_id"] = Guid.NewGuid().ToString(),
            ["created_at_utc"] = "2026-10-30T12:00:00Z",
            ["updated_at_utc"] = "2026-10-30T12:00:00Z",
        };

        if (withProfile)
        {
            row["public_profile_enabled"] = true;
            row["website_url"] = "https://club.example";
            row["instagram_url"] = "https://instagram.com/club";
            row["facebook_url"] = "https://facebook.com/club";
            row["tiktok_url"] = "https://tiktok.com/@club";
            row["youtube_url"] = "https://youtube.com/@club";
            row["whatsapp_phone"] = "+5491122334455";
            row["contact_email"] = "hola@club.example";
        }

        return JsonSerializer.Serialize(new[] { row });
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static (OrganizationRepository Repository, SupabaseOperationContext Ops, HttpClient Http) CreateRepository(
        List<(string Method, string? Body)> requests,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new RecordingHandler(async request =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method.Method, body));
            return respond(request);
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var ops = new SupabaseOperationContext();
        return (new OrganizationRepository(new ClientFactory(client), ops), ops, client);
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
