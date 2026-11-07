using ZonarHub.Domain.Organizations;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Anonymous read side of the public tournament showcase. Implementations filter in the store (never in memory):
/// only tournaments with <c>is_public = true</c>, <c>is_active = true</c>, a public slug, and a status other than
/// <c>cancelled</c> (a tournament without status is not cancelled) are ever returned, from every organization.
/// </summary>
public interface IPublicTournamentReadRepository
{
    /// <summary>Lists up to <paramref name="limit"/> showcase tournaments ordered by start date ascending, then name.</summary>
    Task<IReadOnlyList<PublicTournamentRecord>> ListAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Returns the showcase tournament with this public slug, or null when none satisfies the filter.</summary>
    Task<PublicTournamentRecord?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Public-safe projection of a tournament. Catalog ids are carried only so the application can resolve display
/// names; they are never exposed. Admin-only columns (observations, courts, points, sum value, owner ids) are not read.
/// </summary>
/// <param name="Id">Tournament id, for internal lookups only (its published draw); never part of a public response.</param>
public sealed record PublicTournamentRecord(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    string? Rules,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? RegistrationStartDate,
    DateOnly? RegistrationEndDate,
    string? Status,
    Guid? CategoryId,
    Guid? GenderId,
    Guid? ModalityId,
    int? MaxPairs,
    string? ImageUrl,
    string? CoverImageUrl,
    decimal? RegistrationFeePerPair,
    decimal? PrizeMoney,
    PublicSportRecord? Sport,
    PublicComplexRecord? Complex,
    PublicOrganizationRecord? Organization);

public sealed record PublicSportRecord(string Key, string Name, string Icon, string IconSource);

public sealed record PublicComplexRecord(string Name, string? Address);

/// <summary>
/// The owning organization as stored. Whether it may be shown is decided by the application
/// (active and with <see cref="OrganizationPublicProfile.Enabled"/> consent).
/// </summary>
public sealed record PublicOrganizationRecord(
    string DisplayName,
    string? LogoUrl,
    string? Description,
    string Type,
    bool IsActive,
    OrganizationPublicProfile Profile);
