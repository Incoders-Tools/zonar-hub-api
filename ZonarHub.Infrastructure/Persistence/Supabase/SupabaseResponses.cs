using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>Reading of PostgREST response metadata shared by the repositories: error bodies and exact counts.</summary>
internal static class SupabaseResponses
{
    /// <summary>SQLSTATE the SQL functions raise to refuse a write; PostgREST answers it with HTTP 409.</summary>
    public const string RefusedWriteCode = "PT409";

    public const string UniqueViolationCode = "23505";

    public const string ForeignKeyViolationCode = "23503";

    /// <summary>
    /// The PostgREST error body of a 409 answer (<c>{"code":"23505","message":"duplicate key value ...", ...}</c>);
    /// null for any other status or a body that is not an error object.
    /// </summary>
    public static async Task<PostgrestError?> ReadConflictAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.Conflict)
            return null;

        try
        {
            return JsonSerializer.Deserialize<PostgrestError>(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The total of <c>Prefer: count=exact</c> from <c>Content-Range</c> (<c>0-9/42</c> or <c>*/0</c>); null when absent or
    /// not a number. HttpClient files Content-Range under the content headers.
    /// </summary>
    public static int? ReadTotalCount(HttpResponseMessage response)
    {
        if (!response.Content.Headers.TryGetValues("Content-Range", out var values))
            return null;

        var range = values.FirstOrDefault();
        var slash = range?.LastIndexOf('/') ?? -1;
        return slash >= 0 && int.TryParse(range![(slash + 1)..], out var total) ? total : null;
    }
}

/// <summary>A PostgREST error body. <see cref="Message"/> names the violated constraint for unique and foreign keys.</summary>
internal sealed record PostgrestError(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message)
{
    /// <summary>True for <paramref name="code"/> raised by the constraint (or index) named <paramref name="constraint"/>.</summary>
    public bool Violates(string code, string constraint) =>
        Code == code && Message?.Contains($"\"{constraint}\"", StringComparison.Ordinal) == true;
}
