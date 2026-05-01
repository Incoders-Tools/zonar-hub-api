using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SocialNetworks;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.Update;

public sealed class UpdateSocialNetworkHandler
    : IRequestHandler<UpdateSocialNetworkCommand, Result<SocialNetworkResponse>>
{
    private readonly ISocialNetworkRepository _networks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateSocialNetworkHandler(ISocialNetworkRepository networks, IUnitOfWork unitOfWork, IClock clock)
    {
        _networks = networks;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SocialNetworkResponse>> Handle(
        UpdateSocialNetworkCommand request,
        CancellationToken cancellationToken)
    {
        var network = await _networks.GetByIdAsync(new SocialNetworkId(request.Id), cancellationToken);
        if (network is null)
        {
            return Result.Failure<SocialNetworkResponse>(SocialNetworkErrors.NotFound);
        }

        var updated = network.Update(
            request.Name,
            request.Url,
            request.Description,
            request.FaIcon,
            request.SortOrder,
            request.IsActive,
            _clock.UtcNow);

        if (updated.IsFailure)
        {
            return Result.Failure<SocialNetworkResponse>(updated.Error);
        }

        _networks.Update(network);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialNetworkResponse.FromDomain(network));
    }
}
