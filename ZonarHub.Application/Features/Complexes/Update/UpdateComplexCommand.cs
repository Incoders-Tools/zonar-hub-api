using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Complexes.Update;

/// <summary>Command to update an existing complex/venue.</summary>
public sealed record UpdateComplexCommand(
    Guid Id,
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
    bool IsActive) : IRequest<Result<ComplexResponse>>;
