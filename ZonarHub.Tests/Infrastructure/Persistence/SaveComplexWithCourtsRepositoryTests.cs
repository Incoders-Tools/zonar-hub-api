using System.Net;
using System.Text;
using System.Text.Json;
using ZonarHub.Application.Abstractions;
using ZonarHub.Infrastructure.Persistence.Supabase;
using Xunit;

namespace ZonarHub.Tests.Infrastructure.Persistence;

public class SaveComplexWithCourtsRepositoryTests
{
    [Fact]
    public async Task Save_PostsOneAggregateRpcWithSnakeCasePayloadAndReadsResult()
    {
        var organizationId = Guid.NewGuid();
        var courtId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var complexId = Guid.NewGuid();
        var calls = 0;
        string? path = null;
        string? method = null;
        string? payload = null;
        var handler = new RecordingHandler(async request =>
        {
            calls++;
            path = request.RequestUri!.AbsolutePath;
            method = request.Method.Method;
            payload = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    complex_id = complexId, court_count = 1, court_ids = new[] { courtId }
                }), Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var repository = new ComplexRepository(new ClientFactory(client), new SupabaseOperationContext());
        var result = await repository.SaveWithCourtsAsync(new SaveComplexWithCourtsData(
            null, organizationId, "Venue", "Address", null, null, null, 2, 3,
            null, null, null, true,
            [new SaveCourtData(courtId, "Court", true, "hard", false, [sportId])], [deletedId]));

        Assert.Equal(1, calls);
        Assert.Equal("POST", method);
        Assert.Equal("/rest/v1/rpc/save_complex_with_courts", path);
        using var json = JsonDocument.Parse(payload!);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("p_complex_id").ValueKind);
        Assert.Equal(organizationId, root.GetProperty("p_organization_id").GetGuid());
        Assert.Equal("Venue", root.GetProperty("p_complex").GetProperty("name").GetString());
        Assert.Equal(2, root.GetProperty("p_complex").GetProperty("sort_order").GetInt32());
        var court = root.GetProperty("p_courts")[0];
        Assert.Equal(courtId, court.GetProperty("id").GetGuid());
        Assert.Equal(sportId, court.GetProperty("sport_ids")[0].GetGuid());
        Assert.Equal(deletedId, root.GetProperty("p_delete_court_ids")[0].GetGuid());
        Assert.Equal(complexId, result.ComplexId);
        Assert.Equal(1, result.CourtCount);
        Assert.Equal(courtId, Assert.Single(result.CourtIds));
    }

    [Fact]
    public async Task Save_NullSportsOmittedButEmptySportsPresent()
    {
        string? payload = null;
        var handler = new RecordingHandler(async request =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    complex_id = Guid.NewGuid(), court_count = 2, court_ids = Array.Empty<Guid>()
                }), Encoding.UTF8, "application/json")
            };
        });
        var repository = new ComplexRepository(new ClientFactory(new HttpClient(handler)
            { BaseAddress = new Uri("https://example.invalid") }), new SupabaseOperationContext());
        await repository.SaveWithCourtsAsync(new SaveComplexWithCourtsData(null, Guid.NewGuid(),
            "Venue", "Address", null, null, null, 0, 0, null, null, null, true,
            [new SaveCourtData(null, "Keep", true, null, false, null),
             new SaveCourtData(null, "Clear", true, null, false, [])], []));

        using var json = JsonDocument.Parse(payload!);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("p_complex").GetProperty("description").ValueKind);
        Assert.False(root.GetProperty("p_courts")[0].TryGetProperty("sport_ids", out _));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("p_courts")[1].GetProperty("sport_ids").ValueKind);
        Assert.Empty(root.GetProperty("p_courts")[1].GetProperty("sport_ids").EnumerateArray());
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
