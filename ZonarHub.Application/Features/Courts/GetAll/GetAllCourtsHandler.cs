using ZonarHub.Application.Abstractions;
using MediatR;

namespace ZonarHub.Application.Features.Courts.GetAll;

internal sealed class GetAllCourtsHandler : IRequestHandler<GetAllCourtsQuery, IReadOnlyList<CourtDto>>
{
    private readonly ICourtRepository _courts;

    public GetAllCourtsHandler(ICourtRepository courts)
    {
        _courts = courts;
    }

    public async Task<IReadOnlyList<CourtDto>> Handle(GetAllCourtsQuery request, CancellationToken cancellationToken)
    {
        var courts = await _courts.GetAllAsync(cancellationToken);

        return courts.Select(c => new CourtDto(
            c.Id.Value,
            c.ComplexId.Value,
            c.Name,
            c.IsActive,
            c.CreatedAtUtc))
            .ToList();
    }
}
