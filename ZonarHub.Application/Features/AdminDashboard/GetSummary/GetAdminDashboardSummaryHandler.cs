using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Tournaments;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminDashboard.GetSummary;

public sealed class GetAdminDashboardSummaryHandler
    : IRequestHandler<GetAdminDashboardSummaryQuery, Result<AdminDashboardSummaryResponse>>
{
    private const int UserPageSize = 200;

    private readonly IOrganizationRepository _organizations;
    private readonly IComplexRepository _complexes;
    private readonly ICourtRepository _courts;
    private readonly ITournamentRepository _tournaments;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IOrganizationSportRepository _organizationSports;
    private readonly IRegistrationReadRepository _registrations;

    public GetAdminDashboardSummaryHandler(
        IOrganizationRepository organizations,
        IComplexRepository complexes,
        ICourtRepository courts,
        ITournamentRepository tournaments,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IOrganizationSportRepository organizationSports,
        IRegistrationReadRepository registrations)
    {
        _organizations = organizations;
        _complexes = complexes;
        _courts = courts;
        _tournaments = tournaments;
        _users = users;
        _assignments = assignments;
        _organizationSports = organizationSports;
        _registrations = registrations;
    }

    public async Task<Result<AdminDashboardSummaryResponse>> Handle(
        GetAdminDashboardSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<AdminDashboardSummaryResponse>(AdminDashboardErrors.OrganizationRequired);
        }

        var organizationId = new OrganizationId(request.OrganizationId);
        var organization = await _organizations.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            return Result.Failure<AdminDashboardSummaryResponse>(AdminDashboardErrors.OrganizationNotFound);
        }

        var tournamentsTask = _tournaments.ListByOrganizationAsync(organizationId, cancellationToken);
        var complexesTask = _complexes.ListByOrganizationAsync(organizationId, cancellationToken);
        var sportIdsTask = _organizationSports.GetEnabledSportIdsAsync(organizationId, cancellationToken);
        var registrationsTask = _registrations.CountByOrganizationAsync(request.OrganizationId, cancellationToken);
        var activeAdminsTask = ListUsersByRoleAsync(UserRole.Admin, cancellationToken);
        var activePlayersTask = ListUsersByRoleAsync(UserRole.Player, cancellationToken);

        await Task.WhenAll(
            tournamentsTask,
            complexesTask,
            sportIdsTask,
            registrationsTask,
            activeAdminsTask,
            activePlayersTask);

        var tournaments = tournamentsTask.Result;
        var complexes = complexesTask.Result;
        var enabledSportIds = sportIdsTask.Result;

        var courtCounts = await Task.WhenAll(
            complexes.Select(complex => _courts.ListByComplexIdAsync(complex.Id, cancellationToken)));

        var adminCount = await CountUsersInOrganizationAsync(
            activeAdminsTask.Result,
            request.OrganizationId,
            cancellationToken);

        var playersCount = await CountUsersInOrganizationAsync(
            activePlayersTask.Result,
            request.OrganizationId,
            cancellationToken);

        var summary = new AdminDashboardSummaryResponse(
            OrganizationId: request.OrganizationId,
            TotalTournaments: tournaments.Count,
            ActiveTournaments: tournaments.Count(t => t.Status is TournamentStatus.Upcoming or TournamentStatus.Active),
            FinishedTournaments: tournaments.Count(t => t.Status == TournamentStatus.Finished),
            TotalPlayers: playersCount,
            TotalRegistrations: registrationsTask.Result,
            ComplexCount: complexes.Count,
            CourtCount: courtCounts.Sum(list => list.Count),
            AdminCount: adminCount,
            ActiveSportsCount: enabledSportIds.Count);

        return Result.Success(summary);
    }

    private async Task<IReadOnlyList<User>> ListUsersByRoleAsync(
        UserRole role,
        CancellationToken cancellationToken)
    {
        var page = 1;
        var users = new List<User>();

        while (true)
        {
            var (items, totalCount) = await _users.ListAsync(
                new UserQuery(
                    TenantId: null,
                    Search: null,
                    Role: role,
                    IsActive: true,
                    Page: page,
                    PageSize: UserPageSize),
                cancellationToken);

            if (items.Count == 0)
            {
                break;
            }

            users.AddRange(items);

            if (users.Count >= totalCount)
            {
                break;
            }

            page += 1;
        }

        return users;
    }

    private async Task<int> CountUsersInOrganizationAsync(
        IReadOnlyList<User> users,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return 0;
        }

        var userIds = users.Select(user => user.Id.Value).ToList();
        var assignments = await _assignments.GetOrganizationIdsByUserIdsAsync(userIds, cancellationToken);

        var count = 0;
        foreach (var user in users)
        {
            if (user.OrganizationId == organizationId)
            {
                count += 1;
                continue;
            }

            if (assignments.TryGetValue(user.Id.Value, out var assignedIds) && assignedIds.Contains(organizationId))
            {
                count += 1;
            }
        }

        return count;
    }
}
