using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Tenants;

public sealed class Tenant : Entity<TenantId>
{
    private Tenant(TenantId id) : base(id) { }

    public string Name { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string ContactEmail { get; private set; } = string.Empty;
    public TenantPlanType PlanType { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Tenant> Create(
        TenantId id,
        string name,
        string key,
        string contactEmail,
        TenantPlanType planType,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Tenant>(TenantErrors.NameRequired);

        if (string.IsNullOrWhiteSpace(contactEmail))
            return Result.Failure<Tenant>(TenantErrors.ContactEmailRequired);

        return Result.Success(new Tenant(id)
        {
            Name = name.Trim(),
            Key = string.IsNullOrWhiteSpace(key) ? NormalizeKey(name) : key.Trim(),
            ContactEmail = contactEmail.Trim().ToLowerInvariant(),
            PlanType = planType,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }

    public static Tenant Reconstitute(
        TenantId id,
        string name,
        string key,
        string contactEmail,
        TenantPlanType planType,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new Tenant(id)
        {
            Name = name,
            Key = key,
            ContactEmail = contactEmail,
            PlanType = planType,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };
    }

    private static string NormalizeKey(string name) =>
        name.Trim().ToLowerInvariant()
            .Replace(" ", "_")
            .Replace("-", "_");
}
