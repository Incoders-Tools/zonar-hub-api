using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.SocialNetworks;
using MediatR;

namespace ZonarHub.Application.Features.SocialNetworks.Create;

public sealed class CreateSocialNetworkHandler
    : IRequestHandler<CreateSocialNetworkCommand, Result<SocialNetworkResponse>>
{
    private readonly ISocialNetworkRepository _networks;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateSocialNetworkHandler(ISocialNetworkRepository networks, IUnitOfWork unitOfWork, IClock clock)
    {
        _networks = networks;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SocialNetworkResponse>> Handle(
        CreateSocialNetworkCommand request,
        CancellationToken cancellationToken)
    {
        var key = (request.Key ?? string.Empty).Trim().ToLowerInvariant();
        var existing = await _networks.GetByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<SocialNetworkResponse>(SocialNetworkErrors.KeyAlreadyExists);
        }

        var created = SocialNetwork.Create(
            SocialNetworkId.New(),
            request.Name,
            key,
            request.Url,
            request.Description,
            request.FaIcon,
            request.SortOrder,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<SocialNetworkResponse>(created.Error);
        }

        await _networks.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialNetworkResponse.FromDomain(created.Value));
    }
}
