using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Users;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class UserRepository : IUserRepository
{
    private const string RestPath = "/rest/v1/users";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public UserRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?select=*&id=eq.{id.Value}";
        var rows = await _http.GetFromJsonAsync<List<UserRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailLower = Uri.EscapeDataString(email.Trim().ToLowerInvariant());
        var url = $"{RestPath}?select=*&email=eq.{emailLower}&limit=1";
        var rows = await _http.GetFromJsonAsync<List<UserRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailLower = Uri.EscapeDataString(email.Trim().ToLowerInvariant());
        return ExistsAsync($"email=eq.{emailLower}", cancellationToken);
    }

    public Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalized))
            return Task.FromResult(false);

        var escaped = Uri.EscapeDataString(normalized);
        return ExistsAsync($"phone=eq.{escaped}", cancellationToken);
    }

    public async Task<User?> GetByRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var escaped = Uri.EscapeDataString(token.Trim());
        var url = $"{RestPath}?select=*&refresh_token=eq.{escaped}&limit=1";
        var rows = await _http.GetFromJsonAsync<List<UserRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<User> Items, int TotalCount)> ListAsync(
        UserQuery query,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<string>
        {
            "select=*",
            "order=email.asc"
        };

        if (query.TenantId is { } tenantId)
        {
            parts.Add($"tenant_id=eq.{tenantId}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var needle = Uri.EscapeDataString(query.Search.Trim());
            parts.Add($"or=(email.ilike.*{needle}*,full_name.ilike.*{needle}*)");
        }

        if (query.Role is { } role)
        {
            parts.Add($"role=eq.{Uri.EscapeDataString(ToStorageRole(role))}");
        }

        if (query.IsActive is { } isActive)
        {
            parts.Add($"is_active=eq.{isActive.ToString().ToLowerInvariant()}");
        }

        var offset = Math.Max(0, query.Page - 1) * query.PageSize;
        parts.Add($"offset={offset}");
        parts.Add($"limit={query.PageSize}");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?{string.Join("&", parts)}");
        req.Headers.Add("Prefer", "count=exact");

        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var totalCount = 0;
        if (resp.Headers.TryGetValues("Content-Range", out var crValues))
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

        var rows = await resp.Content.ReadFromJsonAsync<List<UserRow>>(cancellationToken) ?? [];
        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        var row = ToRow(user);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    public void Update(User user)
    {
        var patch = ToPatchRow(user);
        _ops.Enqueue((http, ct) => ExecutePatchAsync(http, user.Id.Value, patch, ct));
    }

    public Task RemoveAsync(UserId id, CancellationToken cancellationToken = default)
    {
        _ops.Enqueue((http, ct) => ExecuteDeleteAsync(http, id.Value, ct));
        return Task.CompletedTask;
    }

    private async Task<bool> ExistsAsync(string filter, CancellationToken cancellationToken)
    {
        var rows = await _http.GetFromJsonAsync<List<UserExistsRow>>(
            $"{RestPath}?select=id&{filter}&limit=1",
            cancellationToken);

        return rows is { Count: > 0 };
    }

    private static async Task ExecuteAddAsync(HttpClient http, UserRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecutePatchAsync(HttpClient http, Guid id, UserPatchRow patch, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{id}");
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(patch);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecuteDeleteAsync(HttpClient http, Guid id, CancellationToken ct)
    {
        using var resp = await http.DeleteAsync($"{RestPath}?id=eq.{id}", ct);
        resp.EnsureSuccessStatusCode();
    }

    private static User ToDomain(UserRow row) =>
        User.Reconstitute(
            new UserId(row.Id),
            row.Email,
            row.FullName,
            row.Phone,
            row.BirthDate,
            row.PasswordHash,
            ParseRole(row.Role),
            row.TenantId,
            row.OrganizationId,
            row.AvatarUrl,
            row.Locale,
            row.DateFormat,
            row.IsEmailVerified,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.VerificationCode,
            row.VerificationCodeExpiresAtUtc,
            row.PasswordResetToken,
            row.PasswordResetTokenExpiresAtUtc,
            row.RefreshToken,
            row.RefreshTokenExpiresAtUtc);

    private static UserRow ToRow(User user) => new(
        user.Id.Value,
        user.Email,
        user.FullName,
        NormalizePhone(user.Phone),
        user.BirthDate,
        user.PasswordHash,
        ToStorageRole(user.Role),
        user.TenantId,
        user.OrganizationId,
        user.AvatarUrl,
        user.Locale,
        user.DateFormat,
        user.IsEmailVerified,
        user.IsActive,
        user.CreatedAtUtc,
        user.UpdatedAtUtc,
        user.VerificationCode,
        user.VerificationCodeExpiresAtUtc,
        user.PasswordResetToken,
        user.PasswordResetTokenExpiresAtUtc,
        user.RefreshToken,
        user.RefreshTokenExpiresAtUtc);

    private static UserPatchRow ToPatchRow(User user) => new(
        user.Email,
        user.FullName,
        NormalizePhone(user.Phone),
        user.BirthDate,
        user.PasswordHash,
        ToStorageRole(user.Role),
        user.TenantId,
        user.OrganizationId,
        user.AvatarUrl,
        user.Locale,
        user.DateFormat,
        user.IsEmailVerified,
        user.IsActive,
        user.UpdatedAtUtc,
        user.VerificationCode,
        user.VerificationCodeExpiresAtUtc,
        user.PasswordResetToken,
        user.PasswordResetTokenExpiresAtUtc,
        user.RefreshToken,
        user.RefreshTokenExpiresAtUtc);

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        return phone
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static UserRole ParseRole(string value) => value.ToLowerInvariant() switch
    {
        "system_admin" => UserRole.SystemAdmin,
        "admin" => UserRole.Admin,
        "user" => UserRole.User,
        "player" => UserRole.Player,
        _ => UserRole.Viewer,
    };

    private static string ToStorageRole(UserRole role) => role switch
    {
        UserRole.SystemAdmin => "system_admin",
        UserRole.Admin => "admin",
        UserRole.User => "user",
        UserRole.Player => "player",
        _ => "viewer",
    };

    private sealed record UserExistsRow([property: JsonPropertyName("id")] Guid Id);

    private sealed record UserRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("birth_date")] DateOnly? BirthDate,
        [property: JsonPropertyName("password_hash")] string PasswordHash,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("tenant_id")] Guid? TenantId,
        [property: JsonPropertyName("organization_id")] Guid? OrganizationId,
        [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
        [property: JsonPropertyName("locale")] string? Locale,
        [property: JsonPropertyName("date_format")] string? DateFormat,
        [property: JsonPropertyName("is_email_verified")] bool IsEmailVerified,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc,
        [property: JsonPropertyName("verification_code")] string? VerificationCode,
        [property: JsonPropertyName("verification_code_expires_at_utc")] DateTime? VerificationCodeExpiresAtUtc,
        [property: JsonPropertyName("password_reset_token")] string? PasswordResetToken,
        [property: JsonPropertyName("password_reset_token_expires_at_utc")] DateTime? PasswordResetTokenExpiresAtUtc,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("refresh_token_expires_at_utc")] DateTime? RefreshTokenExpiresAtUtc);

    private sealed record UserPatchRow(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("phone")] string? Phone,
        [property: JsonPropertyName("birth_date")] DateOnly? BirthDate,
        [property: JsonPropertyName("password_hash")] string PasswordHash,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("tenant_id")] Guid? TenantId,
        [property: JsonPropertyName("organization_id")] Guid? OrganizationId,
        [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
        [property: JsonPropertyName("locale")] string? Locale,
        [property: JsonPropertyName("date_format")] string? DateFormat,
        [property: JsonPropertyName("is_email_verified")] bool IsEmailVerified,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc,
        [property: JsonPropertyName("verification_code")] string? VerificationCode,
        [property: JsonPropertyName("verification_code_expires_at_utc")] DateTime? VerificationCodeExpiresAtUtc,
        [property: JsonPropertyName("password_reset_token")] string? PasswordResetToken,
        [property: JsonPropertyName("password_reset_token_expires_at_utc")] DateTime? PasswordResetTokenExpiresAtUtc,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("refresh_token_expires_at_utc")] DateTime? RefreshTokenExpiresAtUtc);
}
