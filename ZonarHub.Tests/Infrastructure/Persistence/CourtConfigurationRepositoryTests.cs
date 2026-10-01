using System.Net;
using System.Text;
using ZonarHub.Domain.Courts;
using ZonarHub.Infrastructure.Persistence.Supabase;
using Xunit;

namespace ZonarHub.Tests.Infrastructure.Persistence;

public class CourtConfigurationRepositoryTests
{
    [Theory]
    [InlineData("GetAll")]
    [InlineData("GetById")]
    [InlineData("ByComplex")]
    public async Task ReadsConfigurationAndNestedSports(string route)
    {
        var id = Guid.NewGuid();
        var complexId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        string? query = null;
        var handler = new Stub(request =>
        {
            query = request.RequestUri!.Query;
            return $"[{{\"id\":\"{id}\",\"complex_id\":\"{complexId}\",\"name\":\"Indoor\",\"is_active\":true,\"created_at_utc\":\"2025-01-01T00:00:00Z\",\"is_indoor\":true,\"surface_type\":\"hard\",\"court_sports\":[{{\"sport_id\":\"{sportId}\"}}]}}]";
        });
        var repo = new CourtRepository(new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") }), new SupabaseOperationContext());
        var court = route switch
        {
            "GetById" => await repo.GetByIdAsync(new CourtId(id)),
            "ByComplex" => Assert.Single(await repo.ListByComplexIdAsync(new ZonarHub.Domain.Complexes.ComplexId(complexId))),
            _ => Assert.Single(await repo.GetAllAsync())
        };
        Assert.NotNull(court);
        Assert.Contains("court_sports(sport_id)", Uri.UnescapeDataString(query!));
        Assert.True(court.IsIndoor);
        Assert.Equal("hard", court.SurfaceType);
        Assert.Equal(sportId, Assert.Single(court.SportIds));
    }

    [Theory]
    [InlineData("GetAll")]
    [InlineData("GetById")]
    [InlineData("ByComplex")]
    public async Task LegacyRowsWithoutConfigurationUseDefaults(string route)
    {
        var id = Guid.NewGuid();
        var complexId = Guid.NewGuid();
        var handler = new Stub(_ => $"[{{\"id\":\"{id}\",\"complex_id\":\"{complexId}\",\"name\":\"Legacy\",\"is_active\":true,\"created_at_utc\":\"2025-01-01T00:00:00Z\"}}]");
        var repo = new CourtRepository(new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") }), new SupabaseOperationContext());

        var court = route switch
        {
            "GetById" => await repo.GetByIdAsync(new CourtId(id)),
            "ByComplex" => Assert.Single(await repo.ListByComplexIdAsync(new ZonarHub.Domain.Complexes.ComplexId(complexId))),
            _ => Assert.Single(await repo.GetAllAsync())
        };

        Assert.NotNull(court);
        Assert.False(court.IsIndoor);
        Assert.Null(court.SurfaceType);
        Assert.Empty(court.SportIds);
    }

    [Fact]
    public async Task QueuedLegacyNameUpdateDoesNotWriteConfiguration()
    {
        string? payload = null;
        var handler = new Stub(request =>
        {
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Equal(HttpMethod.Patch, request.Method);
            return "{}";
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var ops = new SupabaseOperationContext();
        var repo = new CourtRepository(new Factory(client), ops);
        var court = Court.Reconstitute(new CourtId(Guid.NewGuid()), new ZonarHub.Domain.Complexes.ComplexId(Guid.NewGuid()), "Old", true, DateTime.UtcNow, true, "hard", [Guid.NewGuid()]);
        Assert.True(court.Update("New", true).IsSuccess);

        repo.Update(court);
        Assert.Equal(1, await ops.FlushAsync(client, CancellationToken.None));

        Assert.NotNull(payload);
        using var document = System.Text.Json.JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal("New", root.GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("is_indoor", out _));
        Assert.False(root.TryGetProperty("surface_type", out _));
        Assert.False(root.TryGetProperty("court_sports", out _));
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class Stub(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
    }
}
