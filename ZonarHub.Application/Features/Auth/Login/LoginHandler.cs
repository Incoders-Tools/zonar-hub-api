using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Tenants;
using ZonarHub.Domain.Users;
using MediatR;

namespace ZonarHub.Application.Features.Auth.Login;

public sealed class LoginHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly ITenantRepository _tenants;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public LoginHandler(
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        ITenantRepository tenants,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _users = users;
        _assignments = assignments;
        _tenants = tenants;
        _hasher = hasher;
        _jwt = jwt;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
            return Result.Failure<AuthResponse>(UserErrors.InvalidCredentials);

        if (!user.IsActive)
            return Result.Failure<AuthResponse>(UserErrors.Inactive);

        if (!user.IsEmailVerified)
            return Result.Failure<AuthResponse>(UserErrors.EmailNotVerified);

        var accessToken = _jwt.GenerateAccessToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();
        user.SetRefreshToken(refreshToken, _clock.UtcNow.AddDays(7));
        _users.Update(user);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        AuthTenantDto? tenantDto = null;
        if (user.TenantId is { } tenantGuid)
        {
            var tenant = await _tenants.GetByIdAsync(new TenantId(tenantGuid), cancellationToken);
            if (tenant is not null)
                tenantDto = ToTenantDto(tenant);
        }

        var assignedOrganizationIds = await _assignments.GetOrganizationIdsByUserIdAsync(
            user.Id.Value,
            cancellationToken);

        return Result.Success(BuildResponse(user, accessToken, refreshToken, tenantDto, assignedOrganizationIds));
    }

    private AuthResponse BuildResponse(
        Domain.Users.User user,
        string accessToken,
        string refreshToken,
        AuthTenantDto? tenant,
        IReadOnlyList<Guid> assignedOrganizationIds)
    {
        var manageableOrganizationIds = BuildManageableOrganizationIds(user.OrganizationId, assignedOrganizationIds);

        return
        new(
            accessToken,
            refreshToken,
            "Bearer",
            _jwt.AccessTokenExpiresAt(),
            new AuthUserDto(
                user.Id.Value.ToString(),
                user.Email,
                user.FullName,
                RoleToId(user.Role),
                RoleToString(user.Role),
                user.IsActive,
                user.CreatedAtUtc.ToString("o"),
                user.Phone,
                user.BirthDate?.ToString("yyyy-MM-dd"),
                user.AvatarUrl,
                user.TenantId?.ToString(),
                manageableOrganizationIds.Count > 0
                    ? manageableOrganizationIds.Select(id => id.ToString()).ToArray()
                    : null,
                user.OrganizationId?.ToString(),
                user.Locale,
                user.DateFormat),
            tenant);
    }

    private static IReadOnlyList<Guid> BuildManageableOrganizationIds(
        Guid? currentOrganizationId,
        IReadOnlyList<Guid> assignedOrganizationIds)
    {
        var result = new List<Guid>();

        if (currentOrganizationId.HasValue)
        {
            result.Add(currentOrganizationId.Value);
        }

        foreach (var organizationId in assignedOrganizationIds)
        {
            if (!result.Contains(organizationId))
            {
                result.Add(organizationId);
            }
        }

        return result;
    }

    private static AuthTenantDto ToTenantDto(Domain.Tenants.Tenant t) =>
        new(t.Id.Value.ToString(), t.Name, t.Key, t.ContactEmail, "plan-1", t.PlanType.ToString().ToLowerInvariant());

    private static string RoleToId(UserRole role) => role switch
    {
        UserRole.SystemAdmin => "role_system_admin",
        UserRole.Admin => "role_admin",
        UserRole.User => "role_user",
        UserRole.Player => "role_player",
        _ => "role_viewer",
    };

    private static string RoleToString(UserRole role) => role switch
    {
        UserRole.SystemAdmin => "system_admin",
        UserRole.Admin => "admin",
        UserRole.User => "user",
        UserRole.Player => "player",
        _ => "viewer",
    };
}
