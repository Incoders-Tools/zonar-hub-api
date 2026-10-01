using System.Text.Json.Serialization;

namespace ZonarHub.Application.Abstractions;

/// <summary>Provider-independent aggregate data for a single transactional save.</summary>
public sealed record SaveComplexWithCourtsData(
    Guid? ComplexId, Guid OrganizationId, string Name, string Address,
    string? Key, string? Location, string? Description, int SortOrder, int Preponderance,
    string? LogoImagePath, string? CoverImagePath, string? LayoutDiagramPath, bool IsActive,
    IReadOnlyList<SaveCourtData> Courts, IReadOnlyList<Guid> DeleteCourtIds);

public sealed record SaveCourtData(Guid? Id, string Name, bool IsActive,
    string? SurfaceType, bool IsIndoor, IReadOnlyList<Guid>? SportIds);

public sealed record SavedComplexWithCourtsData(
    [property: JsonPropertyName("complex_id")] Guid ComplexId,
    [property: JsonPropertyName("court_count")] int CourtCount,
    [property: JsonPropertyName("court_ids")] IReadOnlyList<Guid> CourtIds);
