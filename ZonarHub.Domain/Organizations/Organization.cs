using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Organizations;

/// <summary>
/// Tenant-scoped organization aggregate.
/// </summary>
public sealed class Organization : Entity<OrganizationId>
{
    private Organization(OrganizationId id) : base(id) { }

    public Guid TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? LegalName { get; private set; }
    public string? Description { get; private set; }
    public OrganizationType Type { get; private set; }
    public string? LogoUrl { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Organization> Create(
        OrganizationId id,
        Guid tenantId,
        string displayName,
        string? legalName,
        string? description,
        OrganizationType type,
        string? logoUrl,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<Organization>(OrganizationErrors.TenantIdRequired);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure<Organization>(OrganizationErrors.DisplayNameRequired);
        }

        if (!Enum.IsDefined(type))
        {
            return Result.Failure<Organization>(OrganizationErrors.TypeInvalid);
        }

        return Result.Success(new Organization(id)
        {
            TenantId = tenantId,
            DisplayName = displayName.Trim(),
            LegalName = string.IsNullOrWhiteSpace(legalName) ? null : legalName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Type = type,
            LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim(),
            IsActive = true,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }

    public static Organization Reconstitute(
        OrganizationId id,
        Guid tenantId,
        string displayName,
        string? legalName,
        string? description,
        OrganizationType type,
        string? logoUrl,
        bool isActive,
        Guid createdByUserId,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) =>
        new(id)
        {
            TenantId = tenantId,
            DisplayName = displayName,
            LegalName = legalName,
            Description = description,
            Type = type,
            LogoUrl = logoUrl,
            IsActive = isActive,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };

    public Result Update(
        string displayName,
        string? legalName,
        string? description,
        OrganizationType type,
        string? logoUrl,
        bool isActive,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure(OrganizationErrors.DisplayNameRequired);
        }

        if (!Enum.IsDefined(type))
        {
            return Result.Failure(OrganizationErrors.TypeInvalid);
        }

        DisplayName = displayName.Trim();
        LegalName = string.IsNullOrWhiteSpace(legalName) ? null : legalName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Type = type;
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }
}
