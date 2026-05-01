using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.Update;

public sealed record UpdateSocialNetworkCommand(
    Guid Id,
    string Name,
    string? Url,
    string? Description,
    string? FaIcon,
    int SortOrder,
    bool IsActive) : IRequest<Result<SocialNetworkResponse>>;
