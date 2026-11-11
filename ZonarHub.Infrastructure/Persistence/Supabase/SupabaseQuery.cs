namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal static class SupabaseQuery
{
    public static string Value(string value) => Uri.EscapeDataString(value.Trim());

    /// <summary>
    /// <see cref="ContainsPattern"/> for a double-quoted value inside an <c>or=(...)</c> logic tree: also drops the
    /// separators and quoting characters (<c>, ( ) " \</c>) that PostgREST reads after URL decoding, so the search
    /// text cannot add conditions.
    /// </summary>
    public static string? QuotedContainsPattern(string? value) =>
        value is null ? null : ContainsPattern(new string(value.Where(c => c is not (',' or '(' or ')' or '"' or '\\')).ToArray()));

    public static string? ContainsPattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = value
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized)
            ? null
            : Uri.EscapeDataString(sanitized);
    }
}
