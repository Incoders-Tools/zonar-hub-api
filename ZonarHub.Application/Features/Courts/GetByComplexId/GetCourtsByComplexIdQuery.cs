using ZonarHub.Application.Features.Courts.GetAll;
using MediatR;

namespace ZonarHub.Application.Features.Courts.GetByComplexId;

public sealed record GetCourtsByComplexIdQuery(Guid ComplexId) : IRequest<IReadOnlyList<CourtDto>>;
