using ZonarHub.Domain.Complexes;

namespace ZonarHub.Application.Features.Complexes;

/// <summary>Response DTO for a complex (venue).</summary>
public sealed record ComplexResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Key,
    string Address,
    string? Location,
    string? Description,
    int SortOrder,
    int Preponderance,
    string? LogoImagePath,
    string? CoverImagePath,
    string? LayoutDiagramPath,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static ComplexResponse FromDomain(Complex c) => new(
        c.Id.Value,
        c.OrganizationId.Value,
        c.Name,
        c.Key,
        c.Address,
        c.Location,
        c.Description,
        c.SortOrder,
        c.Preponderance,
        c.LogoImagePath,
        c.CoverImagePath,
        c.LayoutDiagramPath,
        c.IsActive,
        c.CreatedAtUtc,
        c.UpdatedAtUtc);
}
