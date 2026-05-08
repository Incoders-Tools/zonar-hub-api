using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Domain.Tournaments;

/// <summary>
/// Torneo organizado por una organización.
/// </summary>
public sealed class Tournament : Entity<TournamentId>
{
    private Tournament(TournamentId id) : base(id) { }

    public OrganizationId OrganizationId { get; private set; }
    public ComplexId? ComplexId { get; private set; }
    public SportId SportId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Key { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public DateOnly? RegistrationStartDate { get; private set; }
    public DateOnly? RegistrationEndDate { get; private set; }
    public TournamentStatus Status { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Tournament Reconstitute(
        TournamentId id,
        OrganizationId organizationId,
        ComplexId? complexId,
        SportId sportId,
        string name,
        string? key,
        DateOnly startDate,
        DateOnly endDate,
        DateOnly? registrationStartDate,
        DateOnly? registrationEndDate,
        TournamentStatus status,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new Tournament(id)
        {
            OrganizationId = organizationId,
            ComplexId = complexId,
            SportId = sportId,
            Name = name,
            Key = key,
            StartDate = startDate,
            EndDate = endDate,
            RegistrationStartDate = registrationStartDate,
            RegistrationEndDate = registrationEndDate,
            Status = status,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };
    }

    public static Result<Tournament> Create(
        TournamentId id,
        OrganizationId organizationId,
        ComplexId? complexId,
        SportId sportId,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        DateTime nowUtc,
        string? key = null,
        DateOnly? registrationStartDate = null,
        DateOnly? registrationEndDate = null)
    {
        if (organizationId.Value == Guid.Empty)
            return Result.Failure<Tournament>(TournamentErrors.OrganizationIdRequired);

        if (sportId.Value == Guid.Empty)
            return Result.Failure<Tournament>(TournamentErrors.SportIdRequired);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Tournament>(TournamentErrors.NameRequired);

        if (endDate < startDate)
            return Result.Failure<Tournament>(TournamentErrors.InvalidDateRange);

        if (registrationStartDate is { } regStart && registrationEndDate is { } regEnd && regEnd < regStart)
            return Result.Failure<Tournament>(TournamentErrors.InvalidDateRange);

        var trimmedName = name.Trim();
        var resolvedKey = string.IsNullOrWhiteSpace(key) ? Slugify(trimmedName) : key.Trim();

        return Result.Success(new Tournament(id)
        {
            OrganizationId = organizationId,
            ComplexId = complexId,
            SportId = sportId,
            Name = trimmedName,
            Key = resolvedKey,
            StartDate = startDate,
            EndDate = endDate,
            RegistrationStartDate = registrationStartDate,
            RegistrationEndDate = registrationEndDate,
            Status = TournamentStatus.Upcoming,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }

    /// <summary>
    /// Lower-snake-case slug derived from the display name. Mirrors the
    /// frontend buildEntityKey routine so values stay comparable.
    /// </summary>
    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_')
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch))
                sb.Append('_');
        }

        return sb.ToString().Trim('_');
    }
}
