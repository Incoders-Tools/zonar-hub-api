using ZonarHub.Application.Features.AdminDashboard.GetSummary;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tournaments;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.AdminDashboard;

internal sealed class AdminDashboardSummaryTestHarness
{
    public AdminDashboardSummaryTestHarness(DateTime nowUtc)
    {
        Clock = new TestClock(nowUtc);

        var organizationStore = new InMemoryOrganizationStore();
        var complexStore = new InMemoryComplexStore();
        var courtStore = new InMemoryCourtStore();
        var tournamentStore = new InMemoryTournamentStore();
        var registrationStore = new InMemoryRegistrationStore();
        var userStore = new InMemoryUserStore();
        var assignmentStore = new InMemoryUserOrganizationAssignmentStore();
        var organizationSportStore = new InMemoryOrganizationSportStore();

        Organizations = new InMemoryOrganizationRepository(organizationStore);
        Complexes = new InMemoryComplexRepository(complexStore);
        Courts = new InMemoryCourtRepository(courtStore);
        Tournaments = new InMemoryTournamentRepository(tournamentStore);
        Registrations = new InMemoryRegistrationReadRepository(registrationStore);
        RegistrationStore = registrationStore;
        Users = new InMemoryUserRepository(userStore);
        Assignments = new InMemoryUserOrganizationAssignmentRepository(assignmentStore);
        OrganizationSports = new InMemoryOrganizationSportRepository(organizationSportStore);

        Summary = new GetAdminDashboardSummaryHandler(
            Organizations,
            Complexes,
            Courts,
            Tournaments,
            Users,
            Assignments,
            OrganizationSports,
            Registrations);
    }

    public TestClock Clock { get; }

    public InMemoryOrganizationRepository Organizations { get; }
    public InMemoryComplexRepository Complexes { get; }
    public InMemoryCourtRepository Courts { get; }
    public InMemoryTournamentRepository Tournaments { get; }
    public InMemoryRegistrationReadRepository Registrations { get; }
    public InMemoryUserRepository Users { get; }
    public InMemoryUserOrganizationAssignmentRepository Assignments { get; }
    public InMemoryOrganizationSportRepository OrganizationSports { get; }

    private InMemoryRegistrationStore RegistrationStore { get; }

    public GetAdminDashboardSummaryHandler Summary { get; }

    public async Task<Organization> SeedOrganizationAsync(Guid tenantId, string name)
    {
        var created = Organization.Create(
            OrganizationId.New(),
            tenantId,
            name,
            legalName: null,
            description: null,
            OrganizationType.Estandar,
            logoUrl: null,
            createdByUserId: Guid.NewGuid(),
            Clock.UtcNow);

        if (created.IsFailure)
        {
            throw new InvalidOperationException(created.Error.Code);
        }

        await Organizations.AddAsync(created.Value, CancellationToken.None);
        return created.Value;
    }

    public async Task<Complex> SeedComplexAsync(Organization organization, string name)
    {
        var created = Complex.Create(
            ComplexId.New(),
            organization.Id,
            name,
            null,
            "Address",
            null,
            null,
            0,
            0,
            null,
            null,
            null,
            Clock.UtcNow);

        if (created.IsFailure)
        {
            throw new InvalidOperationException(created.Error.Code);
        }

        await Complexes.AddAsync(created.Value, CancellationToken.None);
        return created.Value;
    }

    public async Task<Court> SeedCourtAsync(Complex complex, string name)
    {
        var created = Court.Create(CourtId.New(), complex.Id, name, Clock.UtcNow);
        if (created.IsFailure)
        {
            throw new InvalidOperationException(created.Error.Code);
        }

        await Courts.AddAsync(created.Value, CancellationToken.None);
        return created.Value;
    }

    public async Task<Tournament> SeedTournamentAsync(
        Organization organization,
        string name,
        TournamentStatus status,
        Complex? complex = null)
    {
        var tournament = Tournament.Reconstitute(
            TournamentId.New(),
            organization.Id,
            complex?.Id,
            new SportId(Guid.NewGuid()),
            name,
            DateOnly.FromDateTime(Clock.UtcNow.Date),
            DateOnly.FromDateTime(Clock.UtcNow.Date.AddDays(2)),
            status,
            isActive: true,
            createdAtUtc: Clock.UtcNow,
            updatedAtUtc: Clock.UtcNow);

        await Tournaments.AddAsync(tournament, CancellationToken.None);
        return tournament;
    }

    public async Task<User> SeedUserAsync(
        UserRole role,
        string email,
        Organization? primaryOrganization = null,
        bool isActive = true)
    {
        var registered = User.Register(
            UserId.New(),
            email,
            fullName: email,
            phone: null,
            birthDate: null,
            passwordHash: "seed-hash",
            role,
            tenantId: primaryOrganization?.TenantId,
            nowUtc: Clock.UtcNow);

        if (registered.IsFailure)
        {
            throw new InvalidOperationException(registered.Error.Code);
        }

        if (primaryOrganization is not null)
        {
            var assigned = registered.Value.AssignOrganization(primaryOrganization.Id.Value, Clock.UtcNow);
            if (assigned.IsFailure)
            {
                throw new InvalidOperationException(assigned.Error.Code);
            }
        }

        if (!isActive)
        {
            var deactivated = registered.Value.UpdateAdminProfile(
                fullName: registered.Value.FullName,
                phone: null,
                role,
                isActive: false,
                organizationId: primaryOrganization?.Id.Value,
                nowUtc: Clock.UtcNow);

            if (deactivated.IsFailure)
            {
                throw new InvalidOperationException(deactivated.Error.Code);
            }
        }

        await Users.AddAsync(registered.Value, CancellationToken.None);
        return registered.Value;
    }

    public Task AssignOrganizationsAsync(User user, params Organization[] organizations)
    {
        return Assignments.SetOrganizationIdsAsync(
            user.Id.Value,
            organizations.Select(org => org.Id.Value).ToList(),
            CancellationToken.None);
    }

    public Task SetEnabledSportsAsync(Organization organization, int count)
    {
        var ids = Enumerable.Range(1, count)
            .Select(_ => new SportId(Guid.NewGuid()))
            .ToList();

        return OrganizationSports.SetEnabledSportsAsync(organization.Id, ids, CancellationToken.None);
    }

    public Task SeedRegistrationsAsync(Organization organization, int count)
    {
        RegistrationStore.Seed(organization.Id.Value, count);
        return Task.CompletedTask;
    }
}
