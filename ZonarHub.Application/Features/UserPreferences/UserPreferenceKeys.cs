namespace ZonarHub.Application.Features.UserPreferences;

/// <summary>
/// Standard user preference keys used throughout the application.
/// </summary>
public static class UserPreferenceKeys
{
    /// <summary>
    /// Whether the user has skipped the initial onboarding configuration.
    /// Values: "true" | "false"
    /// </summary>
    public const string SkippedOnboarding = "user.onboarding.skipped";

    /// <summary>
    /// Whether the user has skipped the guided tour.
    /// Values: "true" | "false"
    /// </summary>
    public const string SkippedTour = "user.tour.skipped";

    /// <summary>
    /// Whether the chatbot is hidden by user preference.
    /// Values: "true" | "false"
    /// </summary>
    public const string ChatbotHidden = "user.chatbot.hidden";

    /// <summary>
    /// User's preferred theme.
    /// Values: "court-energy" | "clay-match" | "night-arena"
    /// </summary>
    public const string Theme = "user.theme";

    /// <summary>
    /// User's preferred language.
    /// Values: "es" | "en" | "pt"
    /// </summary>
    public const string Language = "user.language";

    /// <summary>
    /// User's preferred date format.
    /// Values: "dd/MM/yyyy" | "MM/dd/yyyy" | "yyyy-MM-dd"
    /// </summary>
    public const string DateFormat = "user.dateFormat";

    /// <summary>
    /// Whether the chatbot is currently in embedded mode in the registrations agent tab.
    /// Values: "true" | "false"
    /// </summary>
    public const string ChatbotEmbeddedInRegistrations = "user.chatbot.embeddedInRegistrations";
}
