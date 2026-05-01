using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Domain.Complexes;

/// <summary>
/// Sede o complejo deportivo perteneciente a una organización.
/// </summary>
public sealed class Complex : Entity<ComplexId>
{
    private Complex(ComplexId id) : base(id) { }

    public OrganizationId OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Complex Reconstitute(
        ComplexId id,
        OrganizationId organizationId,
        string name,
        string address,
        string? location,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new Complex(id)
        {
            OrganizationId = organizationId,
            Name = name,
            Address = address,
            Location = location,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };
    }

    public static Result<Complex> Create(
        ComplexId id,
        OrganizationId organizationId,
        string name,
        string address,
        string? location,
        DateTime nowUtc)
    {
        if (organizationId.Value == Guid.Empty)
            return Result.Failure<Complex>(ComplexErrors.OrganizationIdRequired);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Complex>(ComplexErrors.NameRequired);

        if (string.IsNullOrWhiteSpace(address))
            return Result.Failure<Complex>(ComplexErrors.AddressRequired);

        return Result.Success(new Complex(id)
        {
            OrganizationId = organizationId,
            Name = name.Trim(),
            Address = address.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }
}
