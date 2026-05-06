using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Application.Features.AdminUsers.Create;
using ZonarHub.Application.Features.AdminUsers.Delete;
using ZonarHub.Application.Features.AdminUsers.GetAll;
using ZonarHub.Application.Features.AdminUsers.Update;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;

namespace ZonarHub.Tests.Application.AdminUsers;

internal sealed class AdminUsersTestHarness
{
    public AdminUsersTestHarness(DateTime nowUtc)
    {
        CurrentUser = new TestCurrentUser();
        Clock = new TestClock(nowUtc);

        UserStore = new InMemoryUserStore();
        OrganizationStore = new InMemoryOrganizationStore();
        AssignmentStore = new InMemoryUserOrganizationAssignmentStore();
        PermissionStore = new InMemoryUserOrganizationPermissionStore();

        Users = new InMemoryUserRepository(UserStore);
        Organizations = new InMemoryOrganizationRepository(OrganizationStore);
        Assignments = new InMemoryUserOrganizationAssignmentRepository(AssignmentStore);
        UserPermissions = new InMemoryUserOrganizationPermissionRepository(PermissionStore);
        PermissionCatalog = new InMemorySystemPermissionCatalogRepository();
        PermissionService = new UserPermissionService(Assignments, UserPermissions, PermissionCatalog);
        UnitOfWork = new InMemoryUnitOfWork();
        PasswordHasher = new TestPasswordHasher();

        Create = new CreateAdminUserHandler(
            CurrentUser,
            Users,
            Assignments,
            UserPermissions,
            Organizations,
            PermissionCatalog,
            PermissionService,
            PasswordHasher,
            UnitOfWork,
            Clock);
        Update = new UpdateAdminUserHandler(
            CurrentUser,
            Users,
            Assignments,
            UserPermissions,
            Organizations,
            PermissionCatalog,
            PermissionService,
            UnitOfWork,
            Clock);
        Delete = new DeleteAdminUserHandler(CurrentUser, Users, Assignments, UserPermissions, UnitOfWork);
        List = new GetAdminUsersHandler(CurrentUser, Users, Assignments, Organizations);
    }

    public TestCurrentUser CurrentUser { get; }
    public TestClock Clock { get; }

    public InMemoryUserStore UserStore { get; }
    public InMemoryOrganizationStore OrganizationStore { get; }
    public InMemoryUserOrganizationAssignmentStore AssignmentStore { get; }
    public InMemoryUserOrganizationPermissionStore PermissionStore { get; }

    public InMemoryUserRepository Users { get; }
    public InMemoryOrganizationRepository Organizations { get; }
    public InMemoryUserOrganizationAssignmentRepository Assignments { get; }
    public InMemoryUserOrganizationPermissionRepository UserPermissions { get; }
    public InMemorySystemPermissionCatalogRepository PermissionCatalog { get; }
    public UserPermissionService PermissionService { get; }
    public InMemoryUnitOfWork UnitOfWork { get; }
    public TestPasswordHasher PasswordHasher { get; }

    public CreateAdminUserHandler Create { get; }
    public UpdateAdminUserHandler Update { get; }
    public DeleteAdminUserHandler Delete { get; }
    public GetAdminUsersHandler List { get; }

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

    public async Task<Organization> SeedOrganizationAsync(Guid tenantId, string displayName, Guid? createdByUserId = null)
    {
        var create = Organization.Create(
            OrganizationId.New(),
            tenantId,
            displayName,
            legalName: null,
            description: null,
            OrganizationType.Estandar,
            logoUrl: null,
            createdByUserId ?? Guid.NewGuid(),
            Clock.UtcNow);

        if (create.IsFailure)
        {
            throw new InvalidOperationException(create.Error.Code);
        }

        await Organizations.AddAsync(create.Value, CancellationToken.None);
        return create.Value;
    }
}

internal sealed class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Email { get; private set; }
    public Guid? OrganizationId { get; private set; }

    public void Authenticate(Guid userId, string email, Guid? organizationId = null)
    {
        IsAuthenticated = true;
        UserId = userId;
        Email = email;
        OrganizationId = organizationId;
    }

    public void SignOut()
    {
        IsAuthenticated = false;
        UserId = null;
        Email = null;
        OrganizationId = null;
    }
}

internal sealed class TestPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hash::{password}";

    public bool Verify(string password, string hash) => hash == Hash(password);
}
