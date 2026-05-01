using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Tenants;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class TenantRepository : ITenantRepository
{
    private const string RestPath = "/rest/v1/tenants";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public TenantRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?select=*&id=eq.{id.Value}";
        var rows = await _http.GetFromJsonAsync<List<TenantRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        var row = ToRow(tenant);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    public void Update(Tenant tenant)
    {
        var patch = ToPatchRow(tenant);
        _ops.Enqueue((http, ct) => ExecutePatchAsync(http, tenant.Id.Value, patch, ct));
    }

    private static async Task ExecuteAddAsync(HttpClient http, TenantRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecutePatchAsync(HttpClient http, Guid id, TenantPatchRow patch, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{id}");
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(patch);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static Tenant ToDomain(TenantRow row)
    {
        var planType = ParsePlanType(row.PlanType);
        return Tenant.Reconstitute(
            new TenantId(row.Id),
            row.Name,
            row.Key,
            row.ContactEmail,
            planType,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }

    private static TenantRow ToRow(Tenant tenant) => new(
        tenant.Id.Value,
        tenant.Name,
        tenant.Key,
        tenant.ContactEmail,
        ToStoragePlanType(tenant.PlanType),
        tenant.IsActive,
        tenant.CreatedAtUtc,
        tenant.UpdatedAtUtc);

    private static TenantPatchRow ToPatchRow(Tenant tenant) => new(
        tenant.Name,
        tenant.Key,
        tenant.ContactEmail,
        ToStoragePlanType(tenant.PlanType),
        tenant.IsActive,
        tenant.UpdatedAtUtc);

    private static TenantPlanType ParsePlanType(string value) => value.ToLowerInvariant() switch
    {
        "starter" => TenantPlanType.Starter,
        "pro" => TenantPlanType.Pro,
        "enterprise" => TenantPlanType.Enterprise,
        "single_use" => TenantPlanType.SingleUse,
        "singleuse" => TenantPlanType.SingleUse,
        _ => TenantPlanType.Starter,
    };

    private static string ToStoragePlanType(TenantPlanType value) => value switch
    {
        TenantPlanType.Starter => "starter",
        TenantPlanType.Pro => "pro",
        TenantPlanType.Enterprise => "enterprise",
        TenantPlanType.SingleUse => "single_use",
        _ => "starter",
    };

    private sealed record TenantRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("contact_email")] string ContactEmail,
        [property: JsonPropertyName("plan_type")] string PlanType,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record TenantPatchRow(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("contact_email")] string ContactEmail,
        [property: JsonPropertyName("plan_type")] string PlanType,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
