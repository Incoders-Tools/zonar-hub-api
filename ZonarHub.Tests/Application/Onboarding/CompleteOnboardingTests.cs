using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Onboarding.CompleteOnboarding;
using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tenants;
using ZonarHub.Domain.Users;

namespace ZonarHub.Tests.Application.Onboarding;

public class CompleteOnboardingTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task CompleteOnboarding_CreatesAggregateAndAssignsUserOrganization()
    {
        var h = new OnboardingTestHarness(Now);
        await SeedAdminUserAsync(h, UserId, TenantId);

        var padel = await CreateSportAsync(h, "Padel", "padel", 1);
        var tenis = await CreateSportAsync(h, "Tenis", "tenis", 2);

        var command = new CompleteOnboardingCommand(
            TenantId,
            UserId,
            OrganizationDisplayName: "Club Central",
            OrganizationType: OrganizationType.Circuito,
            SystemSettings: new OnboardingSystemSettingsInput("es", "court-energy", "America/Argentina/Buenos_Aires", "dd/MM/yyyy"),
            Venue: new OnboardingVenueInput("Sede Central", "Av. Siempre Viva 123", "CABA", new[] { "Cancha 1", "Cancha 2" }),
            EnabledSportIds: new[] { padel.Id, tenis.Id },
            Tournament: new OnboardingTournamentInput("Apertura", DateOnly.FromDateTime(Now.Date), DateOnly.FromDateTime(Now.Date.AddDays(2))));

        var result = await h.CompleteOnboarding.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.OrganizationId);
        Assert.NotNull(result.Value.ComplexId);
        Assert.Equal(2, result.Value.CourtIds.Count);
        Assert.Equal(2, result.Value.EnabledSportIds.Count);
        Assert.NotNull(result.Value.TournamentId);

        var user = await h.UserRepository.GetByIdAsync(new UserId(UserId), CancellationToken.None);
        Assert.NotNull(user);
        Assert.Equal(result.Value.OrganizationId, user!.OrganizationId);

        var assignmentIds = await h.UserOrganizationAssignmentRepository
            .GetOrganizationIdsByUserIdAsync(UserId, CancellationToken.None);
        Assert.Single(assignmentIds);
        Assert.Equal(result.Value.OrganizationId, assignmentIds[0]);

        var tenantSports = await h.TenantSportRepository.GetEnabledSportIdsAsync(new TenantId(TenantId), CancellationToken.None);
        Assert.Equal(2, tenantSports.Count);

        var orgSports = await h.OrganizationSportRepository.GetEnabledSportIdsAsync(new OrganizationId(result.Value.OrganizationId), CancellationToken.None);
        Assert.Equal(2, orgSports.Count);

        var complex = await h.ComplexRepository.GetByIdAsync(new ZonarHub.Domain.Complexes.ComplexId(result.Value.ComplexId!.Value), CancellationToken.None);
        Assert.NotNull(complex);

        var settingsLocale = await h.SystemSettingRepository.GetByKeyAsync(
            "app.locale",
            ZonarHub.Domain.SystemSettings.SystemSettingScope.Tenant,
            TenantId,
            userId: null,
            CancellationToken.None);
        Assert.NotNull(settingsLocale);
        Assert.Equal("es", settingsLocale!.Value);
    }

    [Fact]
    public async Task CompleteOnboarding_ReplayAfterSuccess_IsIdempotentAndDoesNotCreateSecondOrganization()
    {
        var h = new OnboardingTestHarness(Now);
        await SeedAdminUserAsync(h, UserId, TenantId);

        var padel = await CreateSportAsync(h, "Padel", "padel", 1);

        var command = new CompleteOnboardingCommand(
            TenantId,
            UserId,
            OrganizationDisplayName: "Club Unico",
            OrganizationType: OrganizationType.Circuito,
            SystemSettings: null,
            Venue: null,
            EnabledSportIds: new[] { padel.Id },
            Tournament: null);

        var first = await h.CompleteOnboarding.Handle(command, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await h.CompleteOnboarding.Handle(command, CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.OrganizationId, second.Value.OrganizationId);

        var assignmentIds = await h.UserOrganizationAssignmentRepository
            .GetOrganizationIdsByUserIdAsync(UserId, CancellationToken.None);
        Assert.Single(assignmentIds);
        Assert.Equal(first.Value.OrganizationId, assignmentIds[0]);

        var listed = await h.OrganizationRepository.ListAsync(
            new OrganizationQuery(TenantId, null, null, null, 1, 50),
            CancellationToken.None);
        Assert.Single(listed.Items);
    }

    private static async Task SeedAdminUserAsync(OnboardingTestHarness h, Guid userId, Guid tenantId)
    {
        var userResult = User.Register(
            new UserId(userId),
            "admin@zonar.dev",
            "Admin User",
            phone: null,
            birthDate: null,
            passwordHash: "hash",
            role: UserRole.Admin,
            tenantId: tenantId,
            nowUtc: Now);

        Assert.True(userResult.IsSuccess);

        await h.UserRepository.AddAsync(userResult.Value, CancellationToken.None);
    }

    private static async Task<ZonarHub.Application.Features.Sports.SportResponse> CreateSportAsync(
        OnboardingTestHarness h,
        string name,
        string key,
        int sortOrder)
    {
        var result = await h.CreateSport.Handle(
            new CreateSportCommand(name, key, "🎾", SportIconSource.Unicode, new[] { Guid.NewGuid() }, sortOrder),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
