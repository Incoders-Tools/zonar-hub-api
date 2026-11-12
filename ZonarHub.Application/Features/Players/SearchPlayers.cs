using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.Players;

/// <summary>One page of the organization's players ordered by last name, first name and id (S3).</summary>
/// <param name="Search">Contains match on first name, last name, email or document (case-insensitive); blank for all.</param>
/// <param name="IsActive">Only active or inactive players; null for both.</param>
public sealed record SearchPlayersQuery(Guid OrganizationId, string? Search, bool? IsActive, int Page, int PageSize)
    : IRequest<Result<PageResult<PlayerResponse>>>;

/// <remarks>
/// Applies the tenant rule of <see cref="TournamentOrganizationAccess"/>: <see cref="AdminTournamentErrors.Forbidden"/> for
/// an unknown organization or one outside the caller's tenant. Page and size are normalized as <see cref="PageRequest"/>.
/// </remarks>
public sealed class SearchPlayersHandler : IRequestHandler<SearchPlayersQuery, Result<PageResult<PlayerResponse>>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IPlayerRepository _players;

    public SearchPlayersHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        IPlayerRepository players)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _players = players;
    }

    public async Task<Result<PageResult<PlayerResponse>>> Handle(SearchPlayersQuery request, CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure<PageResult<PlayerResponse>>(AdminTournamentErrors.Forbidden);

        var page = await _players.SearchAsync(
            new PlayerSearch(new OrganizationId(request.OrganizationId), request.Search, request.IsActive, request.Page, request.PageSize),
            cancellationToken);
        return Result.Success(new PageResult<PlayerResponse>(
            page.Items.Select(PlayerMapper.ToResponse).ToList(), page.Page, page.PageSize, page.TotalCount));
    }
}
