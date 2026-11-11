using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// Dashboard registration count, read from <c>tournament_registrations</c> (D1): the organization's active registrations
/// (pending, confirmed or waitlisted; rejected and cancelled are not counted). The legacy <c>registrations</c> table is no
/// longer read. A store failure or an unreadable count yields 0, so the dashboard still renders.
/// </summary>
internal sealed class RegistrationReadRepository : IRegistrationReadRepository
{
    private const string RestPath = "/rest/v1/tournament_registrations";

    private readonly HttpClient _http;

    public RegistrationReadRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<int> CountByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{RestPath}?select=id&organization_id=eq.{organizationId}&is_active=eq.true&limit=1");
        request.Headers.Add("Prefer", "count=exact");

        using var response = await _http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? SupabaseResponses.ReadTotalCount(response) ?? 0 : 0;
    }
}
