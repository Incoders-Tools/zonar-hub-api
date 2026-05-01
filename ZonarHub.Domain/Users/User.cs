using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Users;

public sealed class User : Entity<UserId>
{
    private User(UserId id) : base(id) { }

    public string Email { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string? Locale { get; private set; }
    public string? DateFormat { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    // Auth state (not exposed to clients)
    public string? VerificationCode { get; private set; }
    public DateTime? VerificationCodeExpiresAtUtc { get; private set; }
    public string? PasswordResetToken { get; private set; }
    public DateTime? PasswordResetTokenExpiresAtUtc { get; private set; }
    public string? RefreshToken { get; private set; }
    public DateTime? RefreshTokenExpiresAtUtc { get; private set; }

    public static Result<User> Register(
        UserId id,
        string email,
        string fullName,
        string? phone,
        DateOnly? birthDate,
        string passwordHash,
        UserRole role,
        Guid? tenantId,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<User>(UserErrors.EmailRequired);

        return Result.Success(new User(id)
        {
            Email = email.Trim().ToLowerInvariant(),
            FullName = fullName.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            BirthDate = birthDate,
            PasswordHash = passwordHash,
            Role = role,
            TenantId = tenantId,
            IsEmailVerified = false,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }

    public static User Reconstitute(
        UserId id,
        string email,
        string fullName,
        string? phone,
        DateOnly? birthDate,
        string passwordHash,
        UserRole role,
        Guid? tenantId,
        Guid? organizationId,
        string? avatarUrl,
        string? locale,
        string? dateFormat,
        bool isEmailVerified,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        string? verificationCode,
        DateTime? verificationCodeExpiresAtUtc,
        string? passwordResetToken,
        DateTime? passwordResetTokenExpiresAtUtc,
        string? refreshToken,
        DateTime? refreshTokenExpiresAtUtc)
    {
        return new User(id)
        {
            Email = email,
            FullName = fullName,
            Phone = phone,
            BirthDate = birthDate,
            PasswordHash = passwordHash,
            Role = role,
            TenantId = tenantId,
            OrganizationId = organizationId,
            AvatarUrl = avatarUrl,
            Locale = locale,
            DateFormat = dateFormat,
            IsEmailVerified = isEmailVerified,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
            VerificationCode = verificationCode,
            VerificationCodeExpiresAtUtc = verificationCodeExpiresAtUtc,
            PasswordResetToken = passwordResetToken,
            PasswordResetTokenExpiresAtUtc = passwordResetTokenExpiresAtUtc,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc = refreshTokenExpiresAtUtc,
        };
    }

    public void SetVerificationCode(string code, DateTime expiresAtUtc)
    {
        VerificationCode = code;
        VerificationCodeExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public Result VerifyEmail(string code, DateTime nowUtc)
    {
        if (VerificationCode is null || VerificationCodeExpiresAtUtc is null ||
            VerificationCodeExpiresAtUtc < nowUtc ||
            !string.Equals(VerificationCode, code, StringComparison.Ordinal))
        {
            return Result.Failure(UserErrors.InvalidOrExpiredCode);
        }

        IsEmailVerified = true;
        VerificationCode = null;
        VerificationCodeExpiresAtUtc = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void SetPasswordResetToken(string token, DateTime expiresAtUtc)
    {
        PasswordResetToken = token;
        PasswordResetTokenExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public Result ResetPassword(string token, string newPasswordHash, DateTime nowUtc)
    {
        if (PasswordResetToken is null || PasswordResetTokenExpiresAtUtc is null ||
            PasswordResetTokenExpiresAtUtc < nowUtc ||
            !string.Equals(PasswordResetToken, token, StringComparison.Ordinal))
        {
            return Result.Failure(UserErrors.InvalidOrExpiredResetToken);
        }

        PasswordHash = newPasswordHash;
        PasswordResetToken = null;
        PasswordResetTokenExpiresAtUtc = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void SetRefreshToken(string token, DateTime expiresAtUtc)
    {
        RefreshToken = token;
        RefreshTokenExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void RevokeRefreshToken()
    {
        RefreshToken = null;
        RefreshTokenExpiresAtUtc = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public Result AssignOrganization(Guid organizationId, DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure(UserErrors.OrganizationIdRequired);
        }

        OrganizationId = organizationId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result UpdateAdminProfile(
        string fullName,
        string? phone,
        UserRole role,
        bool isActive,
        Guid? organizationId,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return Result.Failure(UserErrors.FullNameRequired);
        }

        FullName = fullName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Role = role;
        IsActive = isActive;
        OrganizationId = organizationId;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }
}
