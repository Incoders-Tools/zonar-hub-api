using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class EmailTemplateRepository : IEmailTemplateRepository
{
    private const string RestPath = "/rest/v1/email_templates";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public EmailTemplateRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<EmailTemplate?> GetByIdAsync(EmailTemplateId id, CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?select=*&id=eq.{id.Value}&limit=1";
        var rows = await _http.GetFromJsonAsync<List<EmailTemplateRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<EmailTemplate?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var escaped = Uri.EscapeDataString(key.Trim().ToLowerInvariant());
        var url = $"{RestPath}?select=*&key=eq.{escaped}&limit=1";
        var rows = await _http.GetFromJsonAsync<List<EmailTemplateRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<EmailTemplate> Items, int TotalCount)> ListAsync(
        EmailTemplateQuery query,
        CancellationToken cancellationToken = default)
    {
        var qs = BuildListQueryString(query);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?{qs}");
        req.Headers.Add("Prefer", "count=exact");

        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var totalCount = 0;
        if (resp.Content.Headers.TryGetValues("Content-Range", out var crValues))
        {
            var cr = crValues.FirstOrDefault();
            if (cr is not null)
            {
                var slash = cr.IndexOf('/');
                if (slash >= 0 && int.TryParse(cr[(slash + 1)..], out var total))
                {
                    totalCount = total;
                }
            }
        }

        var rows = await resp.Content.ReadFromJsonAsync<List<EmailTemplateRow>>(cancellationToken) ?? [];
        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public Task AddAsync(EmailTemplate template, CancellationToken cancellationToken = default)
    {
        var row = ToRow(template);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    public void Update(EmailTemplate template)
    {
        var patch = ToPatchRow(template);
        _ops.Enqueue((http, ct) => ExecutePatchAsync(http, template.Id.Value, patch, ct));
    }

    public void Remove(EmailTemplate template)
    {
        _ops.Enqueue((http, ct) => ExecuteDeleteAsync(http, template.Id.Value, ct));
    }

    private static string BuildListQueryString(EmailTemplateQuery query)
    {
        var parts = new List<string>
        {
            "select=*",
            "order=key.asc"
        };

        if (!string.IsNullOrWhiteSpace(query.KeyContains))
        {
            parts.Add($"key=ilike.*{Uri.EscapeDataString(query.KeyContains.Trim())}*");
        }

        if (query.IsActive is { } isActive)
        {
            parts.Add($"is_active=eq.{isActive.ToString().ToLowerInvariant()}");
        }

        if (query.PageSize != int.MaxValue)
        {
            var offset = Math.Max(0, query.Page - 1) * query.PageSize;
            parts.Add($"offset={offset}");
            parts.Add($"limit={query.PageSize}");
        }

        return string.Join("&", parts);
    }

    private static async Task ExecuteAddAsync(HttpClient http, EmailTemplateRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecutePatchAsync(HttpClient http, Guid id, EmailTemplatePatchRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{id}");
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecuteDeleteAsync(HttpClient http, Guid id, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{RestPath}?id=eq.{id}");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static EmailTemplate ToDomain(EmailTemplateRow row) =>
        EmailTemplate.Reconstitute(
            new EmailTemplateId(row.Id),
            row.Key,
            row.Subject,
            row.HtmlBody,
            row.Description,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static EmailTemplateRow ToRow(EmailTemplate template) =>
        new(
            template.Id.Value,
            template.Key,
            template.Subject,
            template.HtmlBody,
            template.Description,
            template.IsActive,
            template.CreatedAtUtc,
            template.UpdatedAtUtc);

    private static EmailTemplatePatchRow ToPatchRow(EmailTemplate template) =>
        new(
            template.Subject,
            template.HtmlBody,
            template.Description,
            template.IsActive,
            template.UpdatedAtUtc);

    private sealed record EmailTemplateRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html_body")] string HtmlBody,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record EmailTemplatePatchRow(
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html_body")] string HtmlBody,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
