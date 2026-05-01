using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Sports;
using MediatR;

namespace ZonarHub.Application.Features.Sports.Create;

public sealed class CreateSportHandler : IRequestHandler<CreateSportCommand, Result<SportResponse>>
{
    private readonly ISportRepository _sports;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateSportHandler(ISportRepository sports, IUnitOfWork unitOfWork, IClock clock)
    {
        _sports = sports;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SportResponse>> Handle(
        CreateSportCommand request,
        CancellationToken cancellationToken)
    {
        var key = (request.Key ?? string.Empty).Trim().ToLowerInvariant();
        var existing = await _sports.GetByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<SportResponse>(SportErrors.KeyAlreadyExists);
        }

        var created = Sport.Create(
            SportId.New(),
            request.Name,
            key,
            request.Icon,
            request.IconSource,
            request.ModalityIds,
            request.SortOrder,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<SportResponse>(created.Error);
        }

        await _sports.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SportResponse.FromDomain(created.Value));
    }
}
