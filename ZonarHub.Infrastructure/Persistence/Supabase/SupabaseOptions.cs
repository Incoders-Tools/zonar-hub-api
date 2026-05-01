namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// Options bound from the <c>Supabase</c> configuration section.
/// Set <c>Persistence:Provider</c> to <c>"Supabase"</c> to activate this provider.
/// </summary>
public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    /// <summary>Project URL, e.g. https://xyz.supabase.co</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Service-role or anon key from the Supabase project settings.</summary>
    public string Key { get; init; } = string.Empty;
}
