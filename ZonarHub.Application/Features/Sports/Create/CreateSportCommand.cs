using ZonarHub.Domain.Common;
using ZonarHub.Domain.Sports;
using MediatR;

namespace ZonarHub.Application.Features.Sports.Create;

public sealed record CreateSportCommand(
    string Name,
    string Key,
    string Icon,
    SportIconSource IconSource,
    IEnumerable<Guid>? ModalityIds,
    int SortOrder) : IRequest<Result<SportResponse>>;
