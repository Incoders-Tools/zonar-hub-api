using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class UserOrganizationAssignmentRepository : IUserOrganizationAssignmentRepository
{
    private const string RestPath = "/rest/v1/user_organization_assignments";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public UserOrganizationAssignmentRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<IReadOnlyList<Guid>> GetOrganizationIdsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<AssignmentRow>>(
            $"{RestPath}?select=organization_id,sort_order&user_id=eq.{userId}&order=sort_order.asc",
            cancellationToken) ?? [];

        return rows.Select(row => row.OrganizationId).Distinct().ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetOrganizationIdsByUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var result = userIds.ToDictionary(id => id, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>());

        if (userIds.Count == 0)
        {
            return result;
        }

        var inClause = string.Join(",", userIds.Select(id => id.ToString()));
        var rows = await _http.GetFromJsonAsync<List<AssignmentUserRow>>(
            $"{RestPath}?select=user_id,organization_id,sort_order&user_id=in.({inClause})&order=sort_order.asc",
            cancellationToken) ?? [];

        foreach (var group in rows.GroupBy(row => row.UserId))
        {
            result[group.Key] = group
                .Select(row => row.OrganizationId)
                .Distinct()
                .ToList();
        }

        return result;
    }

    public Task SetOrganizationIdsAsync(
        Guid userId,
        IReadOnlyList<Guid> organizationIds,
        CancellationToken cancellationToken = default)
    {
        var distinct = organizationIds.Distinct().ToList();

        _ops.Enqueue((http, ct) => DeleteByUserAsync(http, userId, ct));

        if (distinct.Count > 0)
        {
            var rows = distinct
                .Select((id, index) => new AssignmentWriteRow(userId, id, index))
                .ToList();

            _ops.Enqueue((http, ct) => InsertRowsAsync(http, rows, ct));
        }

        return Task.CompletedTask;
    }

    public Task RemoveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _ops.Enqueue((http, ct) => DeleteByUserAsync(http, userId, ct));
        return Task.CompletedTask;
    }

    private static async Task DeleteByUserAsync(HttpClient http, Guid userId, CancellationToken ct)
    {
        using var resp = await http.DeleteAsync($"{RestPath}?user_id=eq.{userId}", ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task InsertRowsAsync(HttpClient http, IReadOnlyList<AssignmentWriteRow> rows, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(rows);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record AssignmentRow(
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("sort_order")] int SortOrder);

    private sealed record AssignmentUserRow(
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("sort_order")] int SortOrder);

    private sealed record AssignmentWriteRow(
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("sort_order")] int SortOrder);
}
