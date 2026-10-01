using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class ComplexRepository : IComplexRepository
{
    private const string RestPath = "/rest/v1/complexes";
    private const string ReadSelect = "select=*,courts(count)";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public ComplexRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<SavedComplexWithCourtsData> SaveWithCourtsAsync(SaveComplexWithCourtsData data, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            p_complex_id = data.ComplexId,
            p_organization_id = data.OrganizationId,
            p_complex = new
            {
                name = data.Name, address = data.Address, key = data.Key,
                location = data.Location, description = data.Description,
                sort_order = data.SortOrder, preponderance = data.Preponderance,
                logo_image_path = data.LogoImagePath, cover_image_path = data.CoverImagePath,
                layout_diagram_path = data.LayoutDiagramPath, is_active = data.IsActive
            },
            p_courts = data.Courts.Select(c =>
            {
                var court = new Dictionary<string, object?>
                {
                    ["id"] = c.Id, ["name"] = c.Name, ["is_active"] = c.IsActive,
                    ["surface_type"] = c.SurfaceType, ["is_indoor"] = c.IsIndoor
                };
                if (c.SportIds is not null) court["sport_ids"] = c.SportIds;
                return court;
            }),
            p_delete_court_ids = data.DeleteCourtIds
        };
        using var response = await _http.PostAsJsonAsync("/rest/v1/rpc/save_complex_with_courts", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SavedComplexWithCourtsData>(cancellationToken)
            ?? throw new InvalidOperationException("Aggregate RPC returned no result.");
    }

    public async Task<Complex?> GetByIdAsync(ComplexId id, CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<ComplexRow>>(
            $"{RestPath}?{ReadSelect}&id=eq.{id.Value}&limit=1",
            cancellationToken);

        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Complex>> ListByOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<ComplexRow>>(
            $"{RestPath}?{ReadSelect}&organization_id=eq.{organizationId.Value}&order=name.asc",
            cancellationToken) ?? [];

        return rows.Select(ToDomain).ToList();
    }

    public Task AddAsync(Complex complex, CancellationToken cancellationToken = default)
    {
        var row = ToRow(complex);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    public async Task UpdateAsync(Complex complex, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            name = complex.Name,
            key = NormalizeOptional(complex.Key),
            address = complex.Address,
            location = NormalizeOptional(complex.Location),
            description = NormalizeOptional(complex.Description),
            sort_order = complex.SortOrder,
            preponderance = complex.Preponderance,
            logo_image_path = NormalizeOptional(complex.LogoImagePath),
            cover_image_path = NormalizeOptional(complex.CoverImagePath),
            layout_diagram_path = NormalizeOptional(complex.LayoutDiagramPath),
            is_active = complex.IsActive,
            updated_at_utc = complex.UpdatedAtUtc
        };

        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{complex.Id.Value}")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("Prefer", "return=minimal");
        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<bool> DeleteAsync(ComplexId id, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{RestPath}?id=eq.{id.Value}");
        using var resp = await _http.SendAsync(req, cancellationToken);
        return resp.IsSuccessStatusCode;
    }

    private static Complex ToDomain(ComplexRow row) =>
        Complex.Reconstitute(
            new ComplexId(row.Id),
            new OrganizationId(row.OrganizationId),
            row.Name,
            row.Key,
            row.Address,
            row.Location,
            row.Description,
            row.SortOrder,
            row.Preponderance,
            row.LogoImagePath,
            row.CoverImagePath,
            row.LayoutDiagramPath,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Courts?.FirstOrDefault()?.Count ?? 0);

    private static ComplexRow ToRow(Complex complex) =>
        new(
            complex.Id.Value,
            complex.OrganizationId.Value,
            complex.Name,
            complex.Key,
            complex.Address,
            complex.Location,
            complex.Description,
            complex.SortOrder,
            complex.Preponderance,
            complex.LogoImagePath,
            complex.CoverImagePath,
            complex.LayoutDiagramPath,
            complex.IsActive,
            complex.CreatedAtUtc,
            complex.UpdatedAtUtc);

    private static async Task ExecuteAddAsync(HttpClient http, ComplexRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record ComplexRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("preponderance")] int Preponderance,
        [property: JsonPropertyName("logo_image_path")] string? LogoImagePath,
        [property: JsonPropertyName("cover_image_path")] string? CoverImagePath,
        [property: JsonPropertyName("layout_diagram_path")] string? LayoutDiagramPath,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc,
        [property: JsonPropertyName("courts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CourtCountRow[]? Courts = null);

    private sealed record CourtCountRow([property: JsonPropertyName("count")] int Count);
}
