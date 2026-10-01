using System.Net;
using System.Text;
using System.Text.Json;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;
using ZonarHub.Infrastructure.Persistence.Supabase;
using Xunit;

namespace ZonarHub.Tests.Infrastructure.Persistence;

public class ComplexCourtCountRepositoryTests
{
    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 0)]
    public async Task ReadsEmbeddedCourtCountInOneUnfilteredRequest(bool single, int count)
    {
        var id = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var calls = 0;
        var handler = new Handler(request =>
        {
            calls++;
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("courts(count)", query);
            Assert.DoesNotContain("is_active", query);
            Assert.Contains(single ? $"id=eq.{id}" : $"organization_id=eq.{organizationId}", query);
            var json = $$"""[{"id":"{{id}}","organization_id":"{{organizationId}}","name":"Venue","address":"Address","is_active":true,"created_at_utc":"2025-01-01T00:00:00Z","updated_at_utc":"2025-01-01T00:00:00Z","courts":[{"count":{{count}}}]}]""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var repository = new ComplexRepository(new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") }), new SupabaseOperationContext());
        var result = single ? await repository.GetByIdAsync(new ComplexId(id)) : Assert.Single(await repository.ListByOrganizationAsync(new OrganizationId(organizationId)));
        Assert.Equal(count, result!.CourtCount);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LegacyMissingAggregateDefaultsToZero()
    {
        var id = Guid.NewGuid();
        var org = Guid.NewGuid();
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""[{"id":"{{id}}","organization_id":"{{org}}","name":"Venue","address":"Address","is_active":true,"created_at_utc":"2025-01-01T00:00:00Z","updated_at_utc":"2025-01-01T00:00:00Z"}]""", Encoding.UTF8, "application/json")
        });
        var repository = new ComplexRepository(new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") }), new SupabaseOperationContext());
        Assert.Equal(0, (await repository.GetByIdAsync(new ComplexId(id)))!.CourtCount);
    }

    [Fact]
    public async Task LegacyPostExcludesEmbeddedCourtsRelation()
    {
        string? payload = null;
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var context = new SupabaseOperationContext();
        var repository = new ComplexRepository(new Factory(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") }), context);
        var complex = Complex.Create(new ComplexId(Guid.NewGuid()), new OrganizationId(Guid.NewGuid()),
            "Venue", null, "Address", null, null, 0, 0, null, null, null, DateTime.UtcNow).Value;
        await repository.AddAsync(complex);
        await context.FlushAsync(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") }, CancellationToken.None);
        using var document = JsonDocument.Parse(payload!);
        Assert.False(document.RootElement.TryGetProperty("courts", out _));
        Assert.Equal("Venue", document.RootElement.GetProperty("name").GetString());
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }
}
