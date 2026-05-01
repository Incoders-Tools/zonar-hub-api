using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Onboarding.CompleteOnboarding;
using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.Onboarding;

internal sealed class OnboardingTestHarness
{
    public OnboardingTestHarness(DateTime nowUtc)
    {
        Clock = new TestClock(nowUtc);
        UnitOfWork = new InMemoryUnitOfWork();

        UserRepository = new InMemoryUserRepository(new InMemoryUserStore());
        OrganizationRepository = new InMemoryOrganizationRepository(new InMemoryOrganizationStore());
        SystemSettingRepository = new InMemorySystemSettingRepository(new InMemorySystemSettingStore());
        ComplexRepository = new InMemoryComplexRepository(new InMemoryComplexStore());
        CourtRepository = new InMemoryCourtRepository(new InMemoryCourtStore());
        SportRepository = new InMemorySportRepository(new InMemorySportStore());
        OrganizationSportRepository = new InMemoryOrganizationSportRepository(new InMemoryOrganizationSportStore());
        TenantSportRepository = new InMemoryTenantSportRepository(new InMemoryTenantSportStore());
        TournamentRepository = new InMemoryTournamentRepository(new InMemoryTournamentStore());

        CompleteOnboarding = new CompleteOnboardingHandler(
            UserRepository,
            OrganizationRepository,
            SystemSettingRepository,
            ComplexRepository,
            CourtRepository,
            SportRepository,
            OrganizationSportRepository,
            TenantSportRepository,
            TournamentRepository,
            UnitOfWork,
            Clock);

        CreateSport = new CreateSportHandler(SportRepository, UnitOfWork, Clock);
    }

    public TestClock Clock { get; }
    public IUnitOfWork UnitOfWork { get; }

    public InMemoryUserRepository UserRepository { get; }
    public InMemoryOrganizationRepository OrganizationRepository { get; }
    public InMemorySystemSettingRepository SystemSettingRepository { get; }
    public InMemoryComplexRepository ComplexRepository { get; }
    public InMemoryCourtRepository CourtRepository { get; }
    public InMemorySportRepository SportRepository { get; }
    public InMemoryOrganizationSportRepository OrganizationSportRepository { get; }
    public InMemoryTenantSportRepository TenantSportRepository { get; }
    public InMemoryTournamentRepository TournamentRepository { get; }

    public CompleteOnboardingHandler CompleteOnboarding { get; }
    public CreateSportHandler CreateSport { get; }
}
