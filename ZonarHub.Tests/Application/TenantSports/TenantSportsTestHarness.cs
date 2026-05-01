using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Application.Features.TenantSports.Get;
using ZonarHub.Application.Features.TenantSports.Set;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.TenantSports;

internal sealed class TenantSportsTestHarness
{
    public TenantSportsTestHarness(DateTime nowUtc)
    {
        SportStore = new InMemorySportStore();
        TenantSportStore = new InMemoryTenantSportStore();

        var sportRepo = new InMemorySportRepository(SportStore);
        var tenantSportRepo = new InMemoryTenantSportRepository(TenantSportStore);
        var uow = new InMemoryUnitOfWork();
        Clock = new TestClock(nowUtc);

        CreateSport = new CreateSportHandler(sportRepo, uow, Clock);
        GetTenantSports = new GetTenantSportsHandler(sportRepo, tenantSportRepo);
        SetTenantSports = new SetTenantSportsHandler(sportRepo, tenantSportRepo, uow);
    }

    public InMemorySportStore SportStore { get; }
    public InMemoryTenantSportStore TenantSportStore { get; }
    public TestClock Clock { get; }

    public CreateSportHandler CreateSport { get; }
    public GetTenantSportsHandler GetTenantSports { get; }
    public SetTenantSportsHandler SetTenantSports { get; }
}
