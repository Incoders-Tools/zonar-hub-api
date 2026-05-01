namespace ZonarHub.Infrastructure.Persistence;

/// <summary>
/// Options bound from the <c>Persistence</c> configuration section.
/// </summary>
public sealed class PersistenceOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Persistence";

    /// <summary>
    /// The persistence provider to activate.
    /// Supported values: <c>InMemory</c>, <c>Supabase</c>.
    /// </summary>
    public string Provider { get; init; } = "InMemory";
}
