using ZonarHub.Domain.Common;
using ZonarHub.Domain.Sports;
using MediatR;

namespace ZonarHub.Application.Features.Sports.Update;

public sealed record UpdateSportCommand(
    Guid Id,
    string Name,
    string Icon,
    SportIconSource IconSource,
    IEnumerable<Guid>? ModalityIds,
    int SortOrder,
    bool IsActive) : IRequest<Result<SportResponse>>;
