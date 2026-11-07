namespace ZonarHub.Domain.Organizations;

/// <summary>
/// The organization's consent to appear in ZonarHub public sections and the contact links shown there.
/// Values are trimmed and blank values are stored as null.
/// </summary>
public sealed record OrganizationPublicProfile
{
    private OrganizationPublicProfile() { }

    /// <summary>No consent and no links (the default for every organization).</summary>
    public static OrganizationPublicProfile Empty { get; } = new();

    public bool Enabled { get; private init; }
    public string? WebsiteUrl { get; private init; }
    public string? InstagramUrl { get; private init; }
    public string? FacebookUrl { get; private init; }
    public string? TiktokUrl { get; private init; }
    public string? YoutubeUrl { get; private init; }
    public string? WhatsappPhone { get; private init; }
    public string? ContactEmail { get; private init; }

    public static OrganizationPublicProfile Create(
        bool enabled,
        string? websiteUrl,
        string? instagramUrl,
        string? facebookUrl,
        string? tiktokUrl,
        string? youtubeUrl,
        string? whatsappPhone,
        string? contactEmail) =>
        new()
        {
            Enabled = enabled,
            WebsiteUrl = Normalize(websiteUrl),
            InstagramUrl = Normalize(instagramUrl),
            FacebookUrl = Normalize(facebookUrl),
            TiktokUrl = Normalize(tiktokUrl),
            YoutubeUrl = Normalize(youtubeUrl),
            WhatsappPhone = Normalize(whatsappPhone),
            ContactEmail = Normalize(contactEmail),
        };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
