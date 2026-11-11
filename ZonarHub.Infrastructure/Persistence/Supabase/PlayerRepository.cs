using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;
using ZonarHub.Domain.Users;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// PostgREST store of players (migration <c>20261110100000_tournament_registrations</c>). Every query carries the
/// organization filter. Writes are immediate; a unique-index violation (HTTP 409, SQLSTATE 23505) becomes
/// <see cref="PersistenceConflictException"/> with the matching <see cref="PlayerErrors"/>; other store failures throw
/// <see cref="HttpRequestException"/>. Timestamps are written and read as UTC.
/// </summary>
internal sealed class PlayerRepository(IHttpClientFactory factory) : IPlayerRepository
{
    private const string RestPath = "/rest/v1/players";
    private const string Order = "order=last_name.asc,first_name.asc,id.asc";

    private static readonly (string Index, Error Error)[] UniqueIndexes =
    [
        ("ux_players_organization_email", PlayerErrors.EmailTaken),
        ("ux_players_organization_document", PlayerErrors.DocumentTaken),
        ("ux_players_organization_user", PlayerErrors.UserTaken),
    ];

    private readonly HttpClient _http = factory.CreateClient(SupabaseHttpClientName.Name);

    public async Task<Player?> GetByIdAsync(OrganizationId organizationId, PlayerId id, CancellationToken cancellationToken = default)
    {
        var rows = await GetRowsAsync($"select=*&id=eq.{id}&organization_id=eq.{organizationId}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDomain(rows[0]);
    }

    public async Task<IReadOnlyList<Player>> GetByIdsAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<PlayerId> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            return [];

        var list = string.Join(",", ids.Distinct());
        var rows = await GetRowsAsync($"select=*&organization_id=eq.{organizationId}&id=in.({list})", cancellationToken);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<PageResult<Player>> SearchAsync(PlayerSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);

        var paging = new PageRequest(search.Page, search.PageSize).Normalize();
        var parts = new List<string> { "select=*", $"organization_id=eq.{search.OrganizationId}" };
        if (SupabaseQuery.QuotedContainsPattern(search.Text) is { } needle)
        {
            var pattern = $"ilike.\"*{needle}*\"";
            parts.Add($"or=(first_name.{pattern},last_name.{pattern},email.{pattern},document_id.{pattern})");
        }

        if (search.IsActive is { } isActive)
            parts.Add($"is_active=eq.{(isActive ? "true" : "false")}");

        parts.Add(Order);
        parts.Add($"offset={paging.Skip}");
        parts.Add($"limit={paging.PageSize}");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?{string.Join("&", parts)}");
        request.Headers.Add("Prefer", "count=exact");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rows = await response.Content.ReadFromJsonAsync<List<PlayerRow>>(cancellationToken) ?? [];
        return new PageResult<Player>(
            rows.Select(ToDomain).ToList(),
            paging.Page,
            paging.PageSize,
            SupabaseResponses.ReadTotalCount(response) ?? 0);
    }

    public Task<Player?> FindByEmailAsync(OrganizationId organizationId, string email, CancellationToken cancellationToken = default) =>
        Player.NormalizeEmail(email) is { } normalized
            ? FindAsync($"organization_id=eq.{organizationId}&email=eq.{SupabaseQuery.Value(normalized)}", cancellationToken)
            : Task.FromResult<Player?>(null);

    public Task<Player?> FindByDocumentAsync(OrganizationId organizationId, string documentId, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(documentId)
            ? Task.FromResult<Player?>(null)
            : FindAsync($"organization_id=eq.{organizationId}&document_id=eq.{SupabaseQuery.Value(documentId)}", cancellationToken);

    public async Task AddAsync(Player player, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        using var request = new HttpRequestMessage(HttpMethod.Post, RestPath) { Content = JsonContent.Create(ToRow(player)) };
        request.Headers.Add("Prefer", "return=minimal");
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureWrittenAsync(response, cancellationToken);
    }

    public async Task UpdateAsync(Player player, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var row = ToRow(player);
        var patch = new PlayerPatchRow(
            row.UserId, row.FirstName, row.LastName, row.Email, row.Phone, row.DocumentId, row.BirthDate, row.GenderId,
            row.CategoryId, row.Club, row.IsActive, row.UpdatedAtUtc);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{player.Id}&organization_id=eq.{player.OrganizationId}")
        {
            Content = JsonContent.Create(patch),
        };
        request.Headers.Add("Prefer", "return=representation");
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureWrittenAsync(response, cancellationToken);

        var rows = await response.Content.ReadFromJsonAsync<List<PlayerRow>>(cancellationToken) ?? [];
        if (rows.Count == 0)
            throw new PersistenceConflictException(PlayerErrors.NotFound);
    }

    private async Task<Player?> FindAsync(string filter, CancellationToken cancellationToken)
    {
        var rows = await GetRowsAsync($"select=*&{filter}&limit=1", cancellationToken);
        return rows.Count == 0 ? null : ToDomain(rows[0]);
    }

    private async Task<List<PlayerRow>> GetRowsAsync(string query, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{RestPath}?{query}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PlayerRow>>(cancellationToken) ?? [];
    }

    private static async Task EnsureWrittenAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (await SupabaseResponses.ReadConflictAsync(response, cancellationToken) is { } conflict)
        {
            foreach (var (index, error) in UniqueIndexes)
            {
                if (conflict.Violates(SupabaseResponses.UniqueViolationCode, index))
                    throw new PersistenceConflictException(error);
            }
        }

        response.EnsureSuccessStatusCode();
    }

    private static Player ToDomain(PlayerRow row) => Player.Reconstitute(
        new PlayerId(row.Id),
        new OrganizationId(row.OrganizationId),
        row.UserId is { } user ? new UserId(user) : null,
        new PlayerDetails(row.FirstName, row.LastName, row.Email, row.Phone, row.DocumentId, row.BirthDate, row.GenderId, row.CategoryId, row.Club),
        row.IsActive,
        row.CreatedAtUtc.UtcDateTime,
        row.UpdatedAtUtc.UtcDateTime);

    private static PlayerRow ToRow(Player player) => new(
        player.Id.Value,
        player.OrganizationId.Value,
        player.UserId?.Value,
        player.FirstName,
        player.LastName,
        player.Email,
        player.Phone,
        player.DocumentId,
        player.BirthDate,
        player.GenderId,
        player.CategoryId,
        player.Club,
        player.IsActive,
        Utc(player.CreatedAtUtc),
        Utc(player.UpdatedAtUtc));

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record PlayerRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("user_id")] Guid? UserId,
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("document_id")] string? DocumentId,
        [property: JsonPropertyName("birth_date")] DateOnly? BirthDate,
        [property: JsonPropertyName("gender_id")] Guid? GenderId,
        [property: JsonPropertyName("category_id")] Guid? CategoryId,
        [property: JsonPropertyName("club")] string? Club,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

    private sealed record PlayerPatchRow(
        [property: JsonPropertyName("user_id")] Guid? UserId,
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("document_id")] string? DocumentId,
        [property: JsonPropertyName("birth_date")] DateOnly? BirthDate,
        [property: JsonPropertyName("gender_id")] Guid? GenderId,
        [property: JsonPropertyName("category_id")] Guid? CategoryId,
        [property: JsonPropertyName("club")] string? Club,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);
}
