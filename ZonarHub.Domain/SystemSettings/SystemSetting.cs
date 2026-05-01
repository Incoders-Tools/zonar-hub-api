using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.SystemSettings;

/// <summary>
/// Aggregate root representing a scoped configuration entry that drives UI / runtime behavior.
/// </summary>
public sealed class SystemSetting : Entity<SystemSettingId>
{
    private SystemSetting(
        SystemSettingId id,
        string key,
        string value,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        Key = key;
        Value = value;
        Scope = scope;
        TenantId = tenantId;
        UserId = userId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public SystemSettingScope Scope { get; private set; }

    public Guid? TenantId { get; private set; }

    public Guid? UserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static SystemSetting Reconstitute(
        SystemSettingId id,
        string key,
        string value,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new SystemSetting(
            id,
            key,
            value,
            scope,
            tenantId,
            userId,
            createdAtUtc,
            updatedAtUtc);
    }

    public static Result<SystemSetting> Create(
        SystemSettingId id,
        string key,
        string value,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        DateTime nowUtc)
    {
        var validation = ValidateInputs(key, value, scope, tenantId, userId);
        if (validation.IsFailure)
        {
            return Result.Failure<SystemSetting>(validation.Error);
        }

        var (normalizedTenantId, normalizedUserId) = NormalizeOwnership(scope, tenantId, userId);

        var setting = new SystemSetting(
            id,
            key.Trim(),
            value,
            scope,
            normalizedTenantId,
            normalizedUserId,
            nowUtc,
            nowUtc);

        return Result.Success(setting);
    }

    public Result Update(
        string value,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId,
        DateTime nowUtc)
    {
        var validation = ValidateInputs(Key, value, scope, tenantId, userId);
        if (validation.IsFailure)
        {
            return validation;
        }

        var (normalizedTenantId, normalizedUserId) = NormalizeOwnership(scope, tenantId, userId);

        Value = value;
        Scope = scope;
        TenantId = normalizedTenantId;
        UserId = normalizedUserId;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    private static Result ValidateInputs(
        string key,
        string value,
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure(SystemSettingErrors.KeyRequired);
        }

        if (string.IsNullOrEmpty(value))
        {
            return Result.Failure(SystemSettingErrors.ValueRequired);
        }

        return scope switch
        {
            SystemSettingScope.Global when tenantId is not null || userId is not null =>
                Result.Failure(SystemSettingErrors.ScopeOwnershipViolation),
            SystemSettingScope.Tenant when tenantId is null =>
                Result.Failure(SystemSettingErrors.TenantIdRequired),
            SystemSettingScope.User when userId is null =>
                Result.Failure(SystemSettingErrors.UserIdRequired),
            _ => Result.Success(),
        };
    }

    private static (Guid? TenantId, Guid? UserId) NormalizeOwnership(
        SystemSettingScope scope,
        Guid? tenantId,
        Guid? userId)
    {
        return scope switch
        {
            SystemSettingScope.Global => (null, null),
            SystemSettingScope.Tenant => (tenantId, null),
            SystemSettingScope.User => (tenantId, userId),
            _ => (tenantId, userId),
        };
    }
}
