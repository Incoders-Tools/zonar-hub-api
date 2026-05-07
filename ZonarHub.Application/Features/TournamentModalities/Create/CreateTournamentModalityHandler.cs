using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.Create;

public sealed class CreateTournamentModalityHandler
    : IRequestHandler<CreateTournamentModalityCommand, Result<TournamentModalityResponse>>
{
    private readonly ITournamentModalityRepository _modalities;

    public CreateTournamentModalityHandler(ITournamentModalityRepository modalities)
    {
        _modalities = modalities;
    }

    public async Task<Result<TournamentModalityResponse>> Handle(
        CreateTournamentModalityCommand request,
        CancellationToken cancellationToken)
    {
        var nameEs = (request.NameEs ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nameEs))
        {
            return Result.Failure<TournamentModalityResponse>(TournamentModalityErrors.NameRequired);
        }

        var key = (request.Key ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<TournamentModalityResponse>(TournamentModalityErrors.KeyRequired);
        }

        var existing = await _modalities.GetByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<TournamentModalityResponse>(TournamentModalityErrors.KeyAlreadyExists);
        }

        var dto = new TournamentModalityDto(
            Guid.NewGuid(),
            nameEs,
            (request.NameEn ?? nameEs).Trim(),
            (request.NamePt ?? nameEs).Trim(),
            key,
            request.SortOrder,
            IsActive: true);

        var inserted = await _modalities.AddAsync(dto, cancellationToken);

        return Result.Success(new TournamentModalityResponse(
            inserted.Id, inserted.NameEs, inserted.NameEn, inserted.NamePt, inserted.Key, inserted.SortOrder, inserted.IsActive));
    }
}
