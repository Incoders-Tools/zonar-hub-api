namespace ZonarHub.Application.Features.UserPreferences;

/// <summary>
/// Response DTO representing user preferences as a dictionary.
/// </summary>
public sealed record UserPreferencesResponse(IDictionary<string, string> Preferences)
{
    public static UserPreferencesResponse Empty() => new(new Dictionary<string, string>());
}
