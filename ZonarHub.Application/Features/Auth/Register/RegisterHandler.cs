using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.Auth;
using ZonarHub.Application.Features.Auth.SendVerificationCode;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Tenants;
using ZonarHub.Domain.Users;
using MediatR;

namespace ZonarHub.Application.Features.Auth.Register;

public sealed class RegisterHandler : IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly ICacheStore _cache;
    private readonly IEmailService _email;
    private readonly IEmailTemplateComposer _templates;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RegisterHandler(
        IUserRepository users,
        ITenantRepository tenants,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        ICacheStore cache,
        IEmailService email,
        IEmailTemplateComposer templates,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _users = users;
        _tenants = tenants;
        _hasher = hasher;
        _jwt = jwt;
        _cache = cache;
        _email = email;
        _templates = templates;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<AuthResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var emailLower = request.Email.Trim().ToLowerInvariant();
        var normalizedPhone = NormalizePhone(request.Phone);
        var now = _clock.UtcNow;
        var providedCode = request.VerificationCode.Trim();
        var isMasterCode = AuthVerificationCodes.IsMasterBypass(providedCode);

        // Re-validate verification code
        var cacheKey = SendVerificationCodeHandler.CacheKey(emailLower);
        string? storedCode = null;
        if (!isMasterCode)
        {
            storedCode = await _cache.GetAsync<string>(cacheKey, cancellationToken);
            if (storedCode is null || !string.Equals(storedCode, providedCode, StringComparison.Ordinal))
                return Result.Failure<AuthResponse>(UserErrors.InvalidOrExpiredCode);
        }

        if (await _users.ExistsByEmailAsync(emailLower, cancellationToken))
            return Result.Failure<AuthResponse>(UserErrors.EmailAlreadyExists);

        if (normalizedPhone is not null &&
            await _users.ExistsByPhoneAsync(normalizedPhone, cancellationToken))
        {
            return Result.Failure<AuthResponse>(UserErrors.PhoneAlreadyExists);
        }

        var tenantName = request.FullName.Trim();

        var tenantResult = Tenant.Create(
            TenantId.New(), tenantName, key: string.Empty, emailLower, TenantPlanType.Starter, now);

        if (tenantResult.IsFailure)
            return Result.Failure<AuthResponse>(tenantResult.Error);

        var tenant = tenantResult.Value;
        await _tenants.AddAsync(tenant, cancellationToken);

        // Create user
        DateOnly? birthDate = null;
        if (!string.IsNullOrWhiteSpace(request.BirthDate) &&
            DateOnly.TryParse(request.BirthDate, out var bd))
        {
            birthDate = bd;
        }

        var userResult = User.Register(
            UserId.New(),
            emailLower,
            request.FullName.Trim(),
            normalizedPhone,
            birthDate,
            _hasher.Hash(request.Password),
            UserRole.Admin,
            tenant.Id.Value,
            now);

        if (userResult.IsFailure)
            return Result.Failure<AuthResponse>(userResult.Error);

        var user = userResult.Value;

        // Mark email as verified (code was already validated)
        var approvedCode = isMasterCode ? AuthVerificationCodes.MasterBypassCode : storedCode!;
        user.SetVerificationCode(approvedCode, now.AddMinutes(1));
        user.VerifyEmail(approvedCode, now);

        var accessToken = _jwt.GenerateAccessToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();
        user.SetRefreshToken(refreshToken, now.AddDays(7));

        await _users.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Consume the verification code
        if (!isMasterCode)
            await _cache.RemoveAsync(cacheKey, cancellationToken);

        // Welcome email (fire-and-forget, don't block registration)
        _ = SendWelcomeEmailAsync(emailLower, user.FullName);

        return Result.Success(BuildResponse(user, accessToken, refreshToken, tenant));
    }

    private AuthResponse BuildResponse(User user, string accessToken, string refreshToken, Tenant tenant) =>
        new(
            accessToken,
            refreshToken,
            "Bearer",
            _jwt.AccessTokenExpiresAt(),
            new AuthUserDto(
                user.Id.Value.ToString(),
                user.Email,
                user.FullName,
                "role_admin",
                "admin",
                user.IsActive,
                user.CreatedAtUtc.ToString("o"),
                user.Phone,
                user.BirthDate?.ToString("yyyy-MM-dd"),
                user.AvatarUrl,
                user.TenantId?.ToString(),
                user.TenantId.HasValue ? [user.TenantId.Value.ToString()] : null,
                null, null, null),
            new AuthTenantDto(
                tenant.Id.Value.ToString(),
                tenant.Name,
                tenant.Key,
                tenant.ContactEmail,
                "plan-1",
                tenant.PlanType.ToString().ToLowerInvariant()));

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        return phone
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private async Task SendWelcomeEmailAsync(string to, string fullName)
    {
        try
        {
            var message = await _templates.ComposeAsync(
                to,
                AuthEmailTemplateKeys.Welcome,
                "¡Bienvenido a ZonarHub!",
                $"<h2>Bienvenido, {fullName}!</h2><p>Tu cuenta fue creada exitosamente.</p>",
                new Dictionary<string, string>
                {
                    ["full_name"] = fullName,
                },
                CancellationToken.None);

            await _email.SendAsync(message, CancellationToken.None);
        }
        catch
        {
            // Registration should not fail if welcome email delivery fails.
        }
    }
}
