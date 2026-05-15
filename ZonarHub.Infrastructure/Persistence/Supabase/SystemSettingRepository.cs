using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.SystemSettings;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class SystemSettingRepository : ISystemSettingRepository
{
    private const string RestPath = "/rest/v1/system_settings";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public SystemSettingRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<SystemSetting?> GetByIdAsync(SystemSettingId id, CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<SystemSettingRow>>(
            $"{RestPath}?select=*&id=eq.{id.Value}&limit=1",
            cancellationToken);

        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<SystemSetting?> GetByKeyAsync(
        string key,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var escapedKey = SupabaseQuery.Value(key);
        var parts = new List<string>
        {
            "select=*",
            $"key=ilike.{escapedKey}",
            $"scope=eq.{ToScopeStorage(scope)}",
            "limit=1"
        };

        parts.Add(tenantId is { } tid ? $"tenant_id=eq.{tid}" : "tenant_id=is.null");
        parts.Add(userId is { } uid ? $"user_id=eq.{uid}" : "user_id=is.null");

        var rows = await _http.GetFromJsonAsync<List<SystemSettingRow>>(
            $"{RestPath}?{string.Join("&", parts)}",
            cancellationToken);

        var row = rows?.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        return string.Equals(row.Key, key.Trim(), StringComparison.OrdinalIgnoreCase)
            ? ToDomain(row)
            : null;
    }

    public async Task<(IReadOnlyList<SystemSetting> Items, int TotalCount)> ListAsync(
        SystemSettingQuery query,
        CancellationToken cancellationToken = default)
    {
        var qs = BuildListQuery(query);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?{qs}");
        req.Headers.Add("Prefer", "count=exact");

        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var totalCount = 0;
        if (resp.Content.Headers.TryGetValues("Content-Range", out var crValues))
        {
            var contentRange = crValues.FirstOrDefault();
            if (contentRange is not null)
            {
                var slash = contentRange.IndexOf('/');
                if (slash >= 0 && int.TryParse(contentRange[(slash + 1)..], out var total))
                {
                    totalCount = total;
                }
            }
        }

        var rows = await resp.Content.ReadFromJsonAsync<List<SystemSettingRow>>(cancellationToken) ?? [];
        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default)
    {
        var row = ToRow(setting);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    public void Update(SystemSetting setting)
    {
        var row = ToPatchRow(setting);
        _ops.Enqueue((http, ct) => ExecutePatchAsync(http, setting.Id.Value, row, ct));
    }

    public void Remove(SystemSetting setting)
    {
        _ops.Enqueue((http, ct) => ExecuteDeleteAsync(http, setting.Id.Value, ct));
    }

    private static string BuildListQuery(SystemSettingQuery query)
    {
        var parts = new List<string>
        {
            "select=*",
            "order=key.asc,created_at_utc.asc"
        };

        if (query.Scope is { } scope)
        {
            parts.Add($"scope=eq.{ToScopeStorage(scope)}");
        }

        if (query.TenantId is { } tenantId)
        {
            parts.Add($"tenant_id=eq.{tenantId}");
        }

        if (query.UserId is { } userId)
        {
            parts.Add($"user_id=eq.{userId}");
        }

        if (!string.IsNullOrWhiteSpace(query.KeyContains))
        {
            var key = SupabaseQuery.ContainsPattern(query.KeyContains);
            if (key is not null)
            {
                parts.Add($"key=ilike.*{key}*");
            }
        }

        if (query.PageSize != int.MaxValue)
        {
            var offset = Math.Max(0, query.Page - 1) * query.PageSize;
            parts.Add($"offset={offset}");
            parts.Add($"limit={query.PageSize}");
        }

        return string.Join("&", parts);
    }

    private static string ToScopeStorage(SystemSettingScope scope) => scope switch
    {
        SystemSettingScope.Global => "global",
        SystemSettingScope.Tenant => "tenant",
        SystemSettingScope.User => "user",
        _ => "global"
    };

    private static SystemSettingScope ParseScope(string value) => value.ToLowerInvariant() switch
    {
        "tenant" => SystemSettingScope.Tenant,
        "user" => SystemSettingScope.User,
        _ => SystemSettingScope.Global,
    };

    private static SystemSetting ToDomain(SystemSettingRow row) =>
        SystemSetting.Reconstitute(
            new SystemSettingId(row.Id),
            row.Key,
            row.Value,
            ParseScope(row.Scope),
            row.TenantId,
            row.UserId,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static SystemSettingRow ToRow(SystemSetting setting) =>
        new(
            setting.Id.Value,
            setting.Key,
            setting.Value,
            ToScopeStorage(setting.Scope),
            setting.TenantId,
            setting.UserId,
            setting.CreatedAtUtc,
            setting.UpdatedAtUtc);

    private static SystemSettingPatchRow ToPatchRow(SystemSetting setting) =>
        new(
            setting.Key,
            setting.Value,
            ToScopeStorage(setting.Scope),
            setting.TenantId,
            setting.UserId,
            setting.UpdatedAtUtc);

    private static async Task ExecuteAddAsync(HttpClient http, SystemSettingRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        await EnsureSuccessOrThrowWithBodyAsync(resp, "POST", RestPath, ct);
    }

    private static async Task ExecutePatchAsync(HttpClient http, Guid id, SystemSettingPatchRow row, CancellationToken ct)
    {
        var url = $"{RestPath}?id=eq.{id}";
        using var req = new HttpRequestMessage(HttpMethod.Patch, url);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        await EnsureSuccessOrThrowWithBodyAsync(resp, "PATCH", url, ct);
    }

    private static async Task ExecuteDeleteAsync(HttpClient http, Guid id, CancellationToken ct)
    {
        var url = $"{RestPath}?id=eq.{id}";
        using var resp = await http.DeleteAsync(url, ct);
        await EnsureSuccessOrThrowWithBodyAsync(resp, "DELETE", url, ct);
    }

    /// <summary>
    /// Replaces the bare EnsureSuccessStatusCode call so the actual Supabase
    /// response body (e.g. the PostgREST error code/message/details) is
    /// captured in the exception message and surfaces in logs. Without this
    /// the only signal was a raw HttpRequestException with the status code,
    /// which masked unique-violation errors as generic 5xx.
    /// </summary>
    private static async Task EnsureSuccessOrThrowWithBodyAsync(
        HttpResponseMessage response,
        string method,
        string url,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            // Body read failed — fall through with empty body.
        }

        throw new HttpRequestException(
            $"Supabase {method} {url} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private sealed record SystemSettingRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("tenant_id")] Guid? TenantId,
        [property: JsonPropertyName("user_id")] Guid? UserId,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record SystemSettingPatchRow(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("tenant_id")] Guid? TenantId,
        [property: JsonPropertyName("user_id")] Guid? UserId,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
