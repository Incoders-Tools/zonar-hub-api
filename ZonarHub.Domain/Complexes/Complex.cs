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
    public string? Key { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public int Preponderance { get; private set; }
    public string? LogoImagePath { get; private set; }
    public string? CoverImagePath { get; private set; }
    public string? LayoutDiagramPath { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Complex Reconstitute(
        ComplexId id,
        OrganizationId organizationId,
        string name,
        string? key,
        string address,
        string? location,
        string? description,
        int sortOrder,
        int preponderance,
        string? logoImagePath,
        string? coverImagePath,
        string? layoutDiagramPath,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new Complex(id)
        {
            OrganizationId = organizationId,
            Name = name,
            Key = key,
            Address = address,
            Location = location,
            Description = description,
            SortOrder = sortOrder,
            Preponderance = preponderance,
            LogoImagePath = logoImagePath,
            CoverImagePath = coverImagePath,
            LayoutDiagramPath = layoutDiagramPath,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };
    }

    public static Result<Complex> Create(
        ComplexId id,
        OrganizationId organizationId,
        string name,
        string? key,
        string address,
        string? location,
        string? description,
        int sortOrder,
        int preponderance,
        string? logoImagePath,
        string? coverImagePath,
        string? layoutDiagramPath,
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
            Key = string.IsNullOrWhiteSpace(key) ? null : key.Trim(),
            Address = address.Trim(),
            Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            SortOrder = sortOrder,
            Preponderance = preponderance,
            LogoImagePath = logoImagePath,
            CoverImagePath = coverImagePath,
            LayoutDiagramPath = layoutDiagramPath,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }
}
