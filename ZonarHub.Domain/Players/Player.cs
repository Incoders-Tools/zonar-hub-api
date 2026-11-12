using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Domain.Players;

/// <summary>
/// A player of one organization (S3): a record the organizer or a public registration creates, optionally linked to a
/// user account. Email (lowercase) and document are unique per organization when present; the store enforces it.
/// </summary>
public sealed class Player : Entity<PlayerId>
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;
    public const int MaxPhoneLength = 40;
    public const int MaxDocumentLength = 40;
    public const int MaxClubLength = 120;

    private Player(PlayerId id) : base(id) { }

    public OrganizationId OrganizationId { get; private set; }
    public UserId? UserId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? DocumentId { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public Guid? GenderId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string? Club { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>First and last name separated by a space.</summary>
    public string DisplayName => $"{FirstName} {LastName}";

    public static Player Reconstitute(
        PlayerId id,
        OrganizationId organizationId,
        UserId? userId,
        PlayerDetails details,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        var player = new Player(id)
        {
            OrganizationId = organizationId,
            UserId = userId,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };
        player.Apply(details);
        return player;
    }

    /// <summary>A new active player; <paramref name="nowUtc"/> also bounds the birth date (no future dates).</summary>
    public static Result<Player> Create(
        PlayerId id,
        OrganizationId organizationId,
        UserId? userId,
        PlayerDetails details,
        DateTime nowUtc)
    {
        if (organizationId.Value == Guid.Empty)
            return Result.Failure<Player>(PlayerErrors.OrganizationIdRequired);

        var normalized = Normalize(details, nowUtc);
        if (normalized.IsFailure)
            return Result.Failure<Player>(normalized.Error);

        var player = new Player(id)
        {
            OrganizationId = organizationId,
            UserId = userId,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        player.Apply(normalized.Value);
        return Result.Success(player);
    }

    /// <summary>Replaces the details, activity and user link; nothing changes when the details are invalid.</summary>
    public Result Update(PlayerDetails details, bool isActive, UserId? userId, DateTime nowUtc)
    {
        var normalized = Normalize(details, nowUtc);
        if (normalized.IsFailure)
            return Result.Failure(normalized.Error);

        Apply(normalized.Value);
        IsActive = isActive;
        UserId = userId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>The stored form of an email: trimmed and lowercase; null when blank.</summary>
    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>
    /// Shape check of an already normalized email: one <c>@</c> between non-empty parts, no whitespace, at most
    /// <see cref="MaxEmailLength"/> characters. Deliverability is proven by the verification code, not here.
    /// </summary>
    public static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@');
        return email.Length <= MaxEmailLength
            && at > 0
            && at < email.Length - 1
            && at == email.LastIndexOf('@')
            && !email.Any(char.IsWhiteSpace);
    }

    private static Result<PlayerDetails> Normalize(PlayerDetails details, DateTime nowUtc)
    {
        var firstName = details.FirstName?.Trim();
        if (string.IsNullOrEmpty(firstName))
            return Result.Failure<PlayerDetails>(PlayerErrors.FirstNameRequired);

        var lastName = details.LastName?.Trim();
        if (string.IsNullOrEmpty(lastName))
            return Result.Failure<PlayerDetails>(PlayerErrors.LastNameRequired);

        if (firstName.Length > MaxNameLength || lastName.Length > MaxNameLength)
            return Result.Failure<PlayerDetails>(PlayerErrors.NameTooLong);

        var email = NormalizeEmail(details.Email);
        if (email is not null && !IsValidEmail(email))
            return Result.Failure<PlayerDetails>(PlayerErrors.InvalidEmail);

        var phone = Optional(details.Phone);
        var document = Optional(details.DocumentId);
        var club = Optional(details.Club);
        if (phone is { Length: > MaxPhoneLength } || document is { Length: > MaxDocumentLength } || club is { Length: > MaxClubLength })
            return Result.Failure<PlayerDetails>(PlayerErrors.FieldTooLong);

        if (details.BirthDate > DateOnly.FromDateTime(nowUtc))
            return Result.Failure<PlayerDetails>(PlayerErrors.BirthDateInFuture);

        return Result.Success(details with
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Phone = phone,
            DocumentId = document,
            Club = club,
        });
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Apply(PlayerDetails details)
    {
        FirstName = details.FirstName;
        LastName = details.LastName;
        Email = details.Email;
        Phone = details.Phone;
        DocumentId = details.DocumentId;
        BirthDate = details.BirthDate;
        GenderId = details.GenderId;
        CategoryId = details.CategoryId;
        Club = details.Club;
    }
}
