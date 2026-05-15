namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal static class SupabaseQuery
{
    public static string Value(string value) => Uri.EscapeDataString(value.Trim());

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
