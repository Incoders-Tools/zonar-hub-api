using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FluentValidation;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Application.Features.Organizations;

/// <summary>
/// Public-profile consent and contact links carried by the organization create and update commands.
/// </summary>
public interface IOrganizationPublicProfileFields
{
    bool PublicProfileEnabled { get; }
    string? WebsiteUrl { get; }
    string? InstagramUrl { get; }
    string? FacebookUrl { get; }
    string? TiktokUrl { get; }
    string? YoutubeUrl { get; }
    string? WhatsappPhone { get; }
    string? ContactEmail { get; }
}

internal static partial class OrganizationPublicProfileFields
{
    public const int UrlMaxLength = 500;
    public const int EmailMaxLength = 254;

    public static OrganizationPublicProfile ToPublicProfile(this IOrganizationPublicProfileFields fields) =>
        OrganizationPublicProfile.Create(
            fields.PublicProfileEnabled,
            fields.WebsiteUrl,
            fields.InstagramUrl,
            fields.FacebookUrl,
            fields.TiktokUrl,
            fields.YoutubeUrl,
            fields.WhatsappPhone,
            fields.ContactEmail);

    /// <summary>
    /// Input rules shared by the create and update validators. Blank values are skipped (they are stored as
    /// null); every other value is checked after trimming.
    /// </summary>
    public static void AddPublicProfileRules<T>(this AbstractValidator<T> validator)
        where T : IOrganizationPublicProfileFields
    {
        AddUrlRule(validator, x => x.WebsiteUrl, "website_url");
        AddUrlRule(validator, x => x.InstagramUrl, "instagram_url");
        AddUrlRule(validator, x => x.FacebookUrl, "facebook_url");
        AddUrlRule(validator, x => x.TiktokUrl, "tiktok_url");
        AddUrlRule(validator, x => x.YoutubeUrl, "youtube_url");

        validator.RuleFor(x => x.WhatsappPhone)
            .Must(value => WhatsappPhonePattern().IsMatch(value!.Trim()))
            .WithMessage("organizations.errors.whatsapp_phone_invalid")
            .When(x => !string.IsNullOrWhiteSpace(x.WhatsappPhone));

        validator.RuleFor(x => x.ContactEmail)
            .Must(value => value!.Trim().Length <= EmailMaxLength)
            .WithMessage("organizations.errors.contact_email_too_long")
            .Must(value => EmailPattern().IsMatch(value!.Trim()))
            .WithMessage("organizations.errors.contact_email_invalid")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }

    private static void AddUrlRule<T>(AbstractValidator<T> validator, Expression<Func<T, string?>> field, string key)
    {
        var read = field.Compile();
        validator.RuleFor(field)
            .Must(value => value!.Trim().Length <= UrlMaxLength)
            .WithMessage($"organizations.errors.{key}_too_long")
            .Must(value => IsHttpUrl(value!.Trim()))
            .WithMessage($"organizations.errors.{key}_invalid")
            .When(x => !string.IsNullOrWhiteSpace(read(x)));
    }

    /// <summary>
    /// Absolute http/https URL with a host. Rejects javascript:, data:, mailto:, ftp: and relative values.
    /// </summary>
    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrEmpty(uri.Host);

    /// <summary>Optional leading plus followed by 8 to 16 digits, nothing else.</summary>
    [GeneratedRegex(@"^\+?[0-9]{8,16}$")]
    private static partial Regex WhatsappPhonePattern();

    /// <summary>One @, no whitespace, and a dot in the domain part.</summary>
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
