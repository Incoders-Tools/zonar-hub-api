using ZonarHub.Application.Features.Organizations.Create;
using ZonarHub.Application.Features.Organizations.Delete;
using ZonarHub.Application.Features.Organizations.GetAll;
using ZonarHub.Application.Features.Organizations.GetById;
using ZonarHub.Application.Features.Organizations.Update;
using ZonarHub.Application.Features.OrganizationSports.Get;
using ZonarHub.Application.Features.OrganizationSports.Set;
using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.Organizations;

internal sealed class OrganizationsTestHarness
{
    public OrganizationsTestHarness(DateTime nowUtc)
    {
        OrgStore = new InMemoryOrganizationStore();
        SportStore = new InMemorySportStore();
        OrgSportStore = new InMemoryOrganizationSportStore();

        var orgRepo = new InMemoryOrganizationRepository(OrgStore);
        var sportRepo = new InMemorySportRepository(SportStore);
        var orgSportRepo = new InMemoryOrganizationSportRepository(OrgSportStore);
        var uow = new InMemoryUnitOfWork();
        Clock = new TestClock(nowUtc);

        CreateOrg = new CreateOrganizationHandler(orgRepo, uow, Clock);
        GetOrgById = new GetOrganizationByIdHandler(orgRepo);
        ListOrgs = new GetOrganizationsHandler(orgRepo);
        UpdateOrg = new UpdateOrganizationHandler(orgRepo, uow, Clock);
        DeleteOrg = new DeleteOrganizationHandler(orgRepo, uow);

        CreateSport = new CreateSportHandler(sportRepo, uow, Clock);
        GetOrgSports = new GetOrganizationSportsHandler(orgRepo, sportRepo, orgSportRepo);
        SetOrgSports = new SetOrganizationSportsHandler(orgRepo, sportRepo, orgSportRepo, uow);
    }

    public InMemoryOrganizationStore OrgStore { get; }
    public InMemorySportStore SportStore { get; }
    public InMemoryOrganizationSportStore OrgSportStore { get; }
    public TestClock Clock { get; }

    public CreateOrganizationHandler CreateOrg { get; }
    public GetOrganizationByIdHandler GetOrgById { get; }
    public GetOrganizationsHandler ListOrgs { get; }
    public UpdateOrganizationHandler UpdateOrg { get; }
    public DeleteOrganizationHandler DeleteOrg { get; }

    public CreateSportHandler CreateSport { get; }
    public GetOrganizationSportsHandler GetOrgSports { get; }
    public SetOrganizationSportsHandler SetOrgSports { get; }
}
