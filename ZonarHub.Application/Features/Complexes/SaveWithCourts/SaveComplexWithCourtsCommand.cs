using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Complexes.SaveWithCourts;

/// <summary>Saves a complex and its explicitly changed courts atomically.</summary>
public sealed record SaveComplexWithCourtsCommand(
    Guid? ComplexId, Guid OrganizationId, string Name, string Address,
    IReadOnlyList<SaveCourtData> Courts, IReadOnlyList<Guid> DeleteCourtIds,
    string? Key = null, string? Location = null, string? Description = null,
    int SortOrder = 0, int Preponderance = 0, string? LogoImagePath = null,
    string? CoverImagePath = null, string? LayoutDiagramPath = null, bool IsActive = true)
    : IRequest<Result<SavedComplexWithCourtsData>>;
