using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Create;

public sealed class CreateTournamentStatusHandler
    : IRequestHandler<CreateTournamentStatusCommand, Result<TournamentStatusResponse>>
{
    private readonly ITournamentStatusRepository _statuses;
    private readonly IClock _clock;

    public CreateTournamentStatusHandler(
        ITournamentStatusRepository statuses,
        IClock clock)
    {
        _statuses = statuses;
        _clock = clock;
    }

    public async Task<Result<TournamentStatusResponse>> Handle(
        CreateTournamentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var nameEs = (request.NameEs ?? string.Empty).Trim();
        var nameEn = (request.NameEn ?? string.Empty).Trim();
        var namePt = (request.NamePt ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(nameEs)
            && string.IsNullOrWhiteSpace(nameEn)
            && string.IsNullOrWhiteSpace(namePt))
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NameRequired);
        }

        var key = (request.Key ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.KeyRequired);
        }

        var existing = await _statuses.GetByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.KeyAlreadyExists);
        }

        // Locale columns are NOT NULL in DB. When the caller (admin user) only
        // provided their active locale we mirror that value into the rest so
        // the row stays consistent until a sysadmin completes the translations.
        var fallbackName = !string.IsNullOrEmpty(nameEs)
            ? nameEs
            : !string.IsNullOrEmpty(nameEn) ? nameEn : namePt;

        var nowUtc = _clock.UtcNow;
        var dto = new TournamentStatusDto(
            Id: Guid.NewGuid(),
            Key: key,
            NameEs: string.IsNullOrEmpty(nameEs) ? fallbackName : nameEs,
            NameEn: string.IsNullOrEmpty(nameEn) ? fallbackName : nameEn,
            NamePt: string.IsNullOrEmpty(namePt) ? fallbackName : namePt,
            DescriptionEs: NullIfBlank(request.DescriptionEs),
            DescriptionEn: NullIfBlank(request.DescriptionEn),
            DescriptionPt: NullIfBlank(request.DescriptionPt),
            SortOrder: request.SortOrder,
            IsActive: true,
            CreatedAt: nowUtc,
            UpdatedAt: nowUtc);

        var inserted = await _statuses.AddAsync(dto, cancellationToken);
        return Result.Success(TournamentStatusMapper.ToResponse(inserted));
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
