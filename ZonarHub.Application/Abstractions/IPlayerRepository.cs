using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Players;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Store of the organization-scoped players (S3, D1). Every read and write is limited to one organization; a player of
/// another organization is reported as missing. Writes are immediate (no unit of work).
/// </summary>
public interface IPlayerRepository
{
    /// <summary>The organization's player, or null.</summary>
    Task<Player?> GetByIdAsync(OrganizationId organizationId, PlayerId id, CancellationToken cancellationToken = default);

    /// <summary>The organization's players among <paramref name="ids"/>, in any order; missing ids are skipped.</summary>
    Task<IReadOnlyList<Player>> GetByIdsAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<PlayerId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>One page of the organization's players ordered by last name, first name and id.</summary>
    Task<PageResult<Player>> SearchAsync(PlayerSearch search, CancellationToken cancellationToken = default);

    /// <summary>The organization's player with this email (compared trimmed and lowercase), or null.</summary>
    Task<Player?> FindByEmailAsync(OrganizationId organizationId, string email, CancellationToken cancellationToken = default);

    /// <summary>The organization's player with this document (compared trimmed and exact), or null.</summary>
    Task<Player?> FindByDocumentAsync(OrganizationId organizationId, string documentId, CancellationToken cancellationToken = default);

    /// <summary>Inserts the player.</summary>
    /// <exception cref="PersistenceConflictException">
    /// <see cref="PlayerErrors.EmailTaken"/>, <see cref="PlayerErrors.DocumentTaken"/> or <see cref="PlayerErrors.UserTaken"/>
    /// when another player of the organization already has it.
    /// </exception>
    Task AddAsync(Player player, CancellationToken cancellationToken = default);

    /// <summary>Writes every editable column of the organization's player.</summary>
    /// <exception cref="PersistenceConflictException">
    /// The unique errors of <see cref="AddAsync"/>, or <see cref="PlayerErrors.NotFound"/> when no player of the
    /// organization has this id.
    /// </exception>
    Task UpdateAsync(Player player, CancellationToken cancellationToken = default);
}

/// <summary>Filter and page of <see cref="IPlayerRepository.SearchAsync"/>.</summary>
/// <param name="Text">Matches first name, last name, email or document (contains, case-insensitive); blank matches all.</param>
/// <param name="IsActive">Only active or inactive players; null for both.</param>
/// <param name="Page">1-based page.</param>
public sealed record PlayerSearch(
    OrganizationId OrganizationId,
    string? Text,
    bool? IsActive,
    int Page,
    int PageSize);
