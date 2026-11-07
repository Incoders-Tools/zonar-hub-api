using System.Net;
using System.Text;
using System.Text.Json;
using ZonarHub.Application.Abstractions;
using ZonarHub.Infrastructure.Persistence.Supabase;

namespace ZonarHub.Tests.Infrastructure.Persistence;

public class TournamentAdminRepositoryTests
{
    private const string PublicSlugViolation =
        """{"code":"23505","details":"Key (public_slug)=(open-cup-abc123) already exists.","hint":null,"message":"duplicate key value violates unique constraint \"ux_tournaments_public_slug\""}""";

    [Fact]
    public async Task Add_SendsVisibilityColumnsAndMapsThemBack()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, request => EchoRows(request));

        var saved = await repository.AddAsync(Dto(isPublic: true, publicSlug: "open-cup-abc123"));

        using var json = JsonDocument.Parse(Assert.Single(requests).Body!);
        Assert.True(json.RootElement.GetProperty("is_public").GetBoolean());
        Assert.Equal("open-cup-abc123", json.RootElement.GetProperty("public_slug").GetString());
        Assert.True(saved.IsPublic);
        Assert.Equal("open-cup-abc123", saved.PublicSlug);
    }

    [Fact]
    public async Task Update_SendsVisibilityColumns()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        var repository = CreateRepository(requests, request => EchoRows(request));

        var saved = await repository.UpdateAsync(Dto(isPublic: false, publicSlug: "open-cup-abc123"));

        var request = Assert.Single(requests);
        Assert.Equal("PATCH", request.Method);
        using var json = JsonDocument.Parse(request.Body!);
        Assert.False(json.RootElement.GetProperty("is_public").GetBoolean());
        Assert.Equal("open-cup-abc123", json.RootElement.GetProperty("public_slug").GetString());
        Assert.False(saved.IsPublic);
        Assert.Equal("open-cup-abc123", saved.PublicSlug);
    }

    [Fact]
    public async Task GetById_RowWithoutVisibilityColumns_MapsToNotPublic()
    {
        var id = Guid.NewGuid();
        var row = $$"""[{"id":"{{id}}","organization_id":"{{Guid.NewGuid()}}","name":"Open","sport_id":"{{Guid.NewGuid()}}","start_date":"2026-06-01","end_date":"2026-06-02","is_active":true,"created_at_utc":"2026-05-01T08:00:00Z","updated_at_utc":"2026-05-01T08:00:00Z"}]""";
        var repository = CreateRepository([], _ => Text(HttpStatusCode.OK, row));

        var tournament = await repository.GetByIdAsync(id);

        Assert.NotNull(tournament);
        Assert.False(tournament.IsPublic);
        Assert.Null(tournament.PublicSlug);
    }

    [Fact]
    public async Task Add_PublicSlugUniqueViolation_ThrowsSlugConflict()
    {
        var repository = CreateRepository([], _ => Text(HttpStatusCode.Conflict, PublicSlugViolation));

        var error = await Assert.ThrowsAsync<TournamentPublicSlugConflictException>(
            () => repository.AddAsync(Dto(isPublic: true, publicSlug: "open-cup-abc123")));

        Assert.Equal("open-cup-abc123", error.PublicSlug);
    }

    [Fact]
    public async Task Update_PublicSlugUniqueViolation_ThrowsSlugConflict()
    {
        var repository = CreateRepository([], _ => Text(HttpStatusCode.Conflict, PublicSlugViolation));

        await Assert.ThrowsAsync<TournamentPublicSlugConflictException>(
            () => repository.UpdateAsync(Dto(isPublic: true, publicSlug: "open-cup-abc123")));
    }

    [Fact]
    public async Task Add_OtherUniqueViolation_KeepsThrowingHttpRequestException()
    {
        const string keyViolation =
            """{"code":"23505","details":"Key (organization_id, key)=(x, open) already exists.","message":"duplicate key value violates unique constraint \"ux_tournaments_org_key\""}""";
        var repository = CreateRepository([], _ => Text(HttpStatusCode.Conflict, keyViolation));

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => repository.AddAsync(Dto(isPublic: true, publicSlug: "open-cup-abc123")));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    [Fact]
    public async Task Add_ConflictWithNonJsonBody_KeepsThrowingHttpRequestException()
    {
        var repository = CreateRepository([], _ => Text(HttpStatusCode.Conflict, "public_slug conflict"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => repository.AddAsync(Dto(isPublic: true, publicSlug: "open-cup-abc123")));
    }

    private static TournamentAdminDto Dto(bool isPublic, string? publicSlug)
    {
        var now = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        return new TournamentAdminDto(
            Guid.NewGuid(), Guid.NewGuid(), "Open Cup", null, null, Guid.NewGuid(), null, null, null, null, null, null,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
            null, null, null, null, null, true, [], now, now, isPublic, publicSlug);
    }

    private static HttpResponseMessage EchoRows(HttpRequestMessage request)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return Text(HttpStatusCode.OK, $"[{body}]");
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static TournamentAdminRepository CreateRepository(
        List<(string Method, string Path, string? Body)> requests,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new RecordingHandler(async request =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
            return respond(request);
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        return new TournamentAdminRepository(new ClientFactory(client));
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
