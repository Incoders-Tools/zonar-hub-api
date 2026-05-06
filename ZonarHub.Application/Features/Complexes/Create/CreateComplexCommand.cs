using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Complexes.Create;

/// <summary>Command to create a new complex/venue.</summary>
public sealed record CreateComplexCommand(
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
    string? LayoutDiagramPath) : IRequest<Result<ComplexResponse>>;
