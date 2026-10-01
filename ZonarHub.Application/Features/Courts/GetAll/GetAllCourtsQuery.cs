using MediatR;

namespace ZonarHub.Application.Features.Courts.GetAll;

public sealed record GetAllCourtsQuery() : IRequest<IReadOnlyList<CourtDto>>;

public sealed record CourtDto(
    Guid Id,
    Guid ComplexId,
    string Name,
    bool IsActive,
    DateTime CreatedAtUtc,
    bool IsIndoor = false,
    string? SurfaceType = null,
    IReadOnlyList<Guid>? SportIds = null);
