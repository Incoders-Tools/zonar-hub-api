using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.Create;

public sealed record CreateSocialNetworkCommand(
    string Name,
    string Key,
    string? Url,
    string? Description,
    string? FaIcon,
    int SortOrder) : IRequest<Result<SocialNetworkResponse>>;
