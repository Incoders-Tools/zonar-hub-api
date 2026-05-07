using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentModalities.Update;

public sealed class UpdateTournamentModalityHandler
    : IRequestHandler<UpdateTournamentModalityCommand, Result<TournamentModalityResponse>>
{
    private readonly ITournamentModalityRepository _modalities;

    public UpdateTournamentModalityHandler(ITournamentModalityRepository modalities)
    {
        _modalities = modalities;
    }

    public async Task<Result<TournamentModalityResponse>> Handle(
        UpdateTournamentModalityCommand request,
        CancellationToken cancellationToken)
    {
        var current = await _modalities.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure<TournamentModalityResponse>(TournamentModalityErrors.NotFound);
        }

        var nameEs = (request.NameEs ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nameEs))
        {
            return Result.Failure<TournamentModalityResponse>(TournamentModalityErrors.NameRequired);
        }

        var dto = current with
        {
            NameEs = nameEs,
            NameEn = (request.NameEn ?? nameEs).Trim(),
            NamePt = (request.NamePt ?? nameEs).Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive
        };

        var updated = await _modalities.UpdateAsync(dto, cancellationToken);

        return Result.Success(new TournamentModalityResponse(
            updated.Id, updated.NameEs, updated.NameEn, updated.NamePt, updated.Key, updated.SortOrder, updated.IsActive));
    }
}
