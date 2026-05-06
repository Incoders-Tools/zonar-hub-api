using ZonarHub.Application.Features.Organizations.Create;
using ZonarHub.Application.Features.Organizations.Delete;
using ZonarHub.Application.Features.Organizations.GetAll;
using ZonarHub.Application.Features.Organizations.GetById;
using ZonarHub.Application.Features.Organizations.Update;
using ZonarHub.Application.Features.OrganizationSports.Get;
using ZonarHub.Application.Features.OrganizationSports.Set;
using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Domain.Users;
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
        UserStore = new InMemoryUserStore();
        AssignmentStore = new InMemoryUserOrganizationAssignmentStore();

        var orgRepo = new InMemoryOrganizationRepository(OrgStore);
        var sportRepo = new InMemorySportRepository(SportStore);
        var orgSportRepo = new InMemoryOrganizationSportRepository(OrgSportStore);
        var userRepo = new InMemoryUserRepository(UserStore);
        var assignmentRepo = new InMemoryUserOrganizationAssignmentRepository(AssignmentStore);
        var uow = new InMemoryUnitOfWork();
        Clock = new TestClock(nowUtc);

        Users = userRepo;
        Assignments = assignmentRepo;

        CreateOrg = new CreateOrganizationHandler(orgRepo, userRepo, assignmentRepo, uow, Clock);
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
    public InMemoryUserStore UserStore { get; }
    public InMemoryUserOrganizationAssignmentStore AssignmentStore { get; }
    public TestClock Clock { get; }
    public InMemoryUserRepository Users { get; }
    public InMemoryUserOrganizationAssignmentRepository Assignments { get; }

    public CreateOrganizationHandler CreateOrg { get; }
    public GetOrganizationByIdHandler GetOrgById { get; }
    public GetOrganizationsHandler ListOrgs { get; }
    public UpdateOrganizationHandler UpdateOrg { get; }
    public DeleteOrganizationHandler DeleteOrg { get; }

    public CreateSportHandler CreateSport { get; }
    public GetOrganizationSportsHandler GetOrgSports { get; }
    public SetOrganizationSportsHandler SetOrgSports { get; }

    public async Task<User> SeedUserAsync(UserRole role, Guid? tenantId, string email, string fullName)
    {
        var create = User.Register(
            UserId.New(),
            email,
            fullName,
            phone: null,
            birthDate: null,
            passwordHash: "seed-hash",
            role,
            tenantId,
            Clock.UtcNow);

        if (create.IsFailure)
        {
            throw new InvalidOperationException(create.Error.Code);
        }

        await Users.AddAsync(create.Value, CancellationToken.None);
        return create.Value;
    }
}
