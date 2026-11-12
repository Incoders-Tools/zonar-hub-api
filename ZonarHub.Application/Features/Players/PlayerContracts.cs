using ZonarHub.Domain.Players;

namespace ZonarHub.Application.Features.Players;

/// <summary>
/// Body of <c>POST</c> and <c>PUT /api/admin/organizations/{organizationId}/players</c> (S3). Text is trimmed and the
/// email stored lowercase; blank optional values are stored as null.
/// </summary>
/// <param name="FirstName">Required, up to 100 characters.</param>
/// <param name="LastName">Required, up to 100 characters.</param>
/// <param name="Email">Optional; unique per organization (any case).</param>
/// <param name="Phone">Optional, up to 40 characters.</param>
/// <param name="DocumentId">Optional, up to 40 characters; unique per organization.</param>
/// <param name="BirthDate">Optional ISO date, not in the future; age rules (D3) need it.</param>
/// <param name="GenderId">Optional id of the <c>genders</c> catalog.</param>
/// <param name="CategoryId">Optional id of the <c>categories</c> catalog.</param>
/// <param name="Club">Optional, up to 120 characters; the draw separation key of its pairs (D5).</param>
/// <param name="IsActive">Only read on update: false deactivates the player (new registrations refuse it).</param>
public sealed record PlayerBody(
    string? FirstName,
    string? LastName,
    string? Email = null,
    string? Phone = null,
    string? DocumentId = null,
    DateOnly? BirthDate = null,
    Guid? GenderId = null,
    Guid? CategoryId = null,
    string? Club = null,
    bool IsActive = true);

/// <summary>A player of the organization. Timestamps are UTC.</summary>
/// <param name="UserId">Linked user account, when the player claimed one.</param>
/// <param name="DisplayName">First and last name separated by a space.</param>
public sealed record PlayerResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? UserId,
    string FirstName,
    string LastName,
    string DisplayName,
    string? Email,
    string? Phone,
    string? DocumentId,
    DateOnly? BirthDate,
    Guid? GenderId,
    Guid? CategoryId,
    string? Club,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

internal static class PlayerMapper
{
    public static PlayerResponse ToResponse(Player player) => new(
        player.Id.Value,
        player.OrganizationId.Value,
        player.UserId?.Value,
        player.FirstName,
        player.LastName,
        player.DisplayName,
        player.Email,
        player.Phone,
        player.DocumentId,
        player.BirthDate,
        player.GenderId,
        player.CategoryId,
        player.Club,
        player.IsActive,
        player.CreatedAtUtc,
        player.UpdatedAtUtc);

    public static PlayerDetails ToDetails(PlayerBody body) => new(
        body.FirstName!,
        body.LastName!,
        body.Email,
        body.Phone,
        body.DocumentId,
        body.BirthDate,
        body.GenderId,
        body.CategoryId,
        body.Club);
}
