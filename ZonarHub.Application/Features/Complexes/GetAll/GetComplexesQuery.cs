using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.Complexes.GetAll;

/// <summary>Returns all complexes for a given organization.</summary>
public sealed record GetComplexesQuery(Guid OrganizationId) : IRequest<Result<IReadOnlyList<ComplexResponse>>>;
