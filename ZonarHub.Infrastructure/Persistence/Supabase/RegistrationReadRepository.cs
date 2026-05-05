using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class RegistrationReadRepository : IRegistrationReadRepository
{
    private const string RestPath = "/rest/v1/registrations";

    private readonly HttpClient _http;

    public RegistrationReadRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<int> CountByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{RestPath}?select=id&organization_id=eq.{organizationId}&limit=1");
        request.Headers.Add("Prefer", "count=exact");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        if (!response.Headers.TryGetValues("Content-Range", out var values))
        {
            return 0;
        }

        var contentRange = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(contentRange))
        {
            return 0;
        }

        var slashIndex = contentRange.LastIndexOf('/');
        if (slashIndex < 0 || slashIndex == contentRange.Length - 1)
        {
            return 0;
        }

        var totalToken = contentRange[(slashIndex + 1)..];
        return int.TryParse(totalToken, out var total) ? total : 0;
    }
}