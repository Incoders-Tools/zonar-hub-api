using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.TournamentStatuses.Update;

public sealed class UpdateTournamentStatusHandler
    : IRequestHandler<UpdateTournamentStatusCommand, Result<TournamentStatusResponse>>
{
    private readonly ITournamentStatusRepository _statuses;
    private readonly IClock _clock;

    public UpdateTournamentStatusHandler(
        ITournamentStatusRepository statuses,
        IClock clock)
    {
        _statuses = statuses;
        _clock = clock;
    }

    public async Task<Result<TournamentStatusResponse>> Handle(
        UpdateTournamentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var current = await _statuses.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure<TournamentStatusResponse>(TournamentStatusCatalogErrors.NotFound);
        }

        // Patch semantics: only the locales the caller sent get overwritten.
        // Empty / whitespace strings preserve the previous value so an admin
        // editing only their active locale doesn't blank the others.
        var nameEs = NonBlankOrFallback(request.NameEs, current.NameEs);
        var nameEn = NonBlankOrFallback(request.NameEn, current.NameEn);
        var namePt = NonBlankOrFallback(request.NamePt, current.NamePt);

        var dto = current with
        {
            NameEs = nameEs,
            NameEn = nameEn,
            NamePt = namePt,
            DescriptionEs = NullIfBlankPreserve(request.DescriptionEs, current.DescriptionEs),
            DescriptionEn = NullIfBlankPreserve(request.DescriptionEn, current.DescriptionEn),
            DescriptionPt = NullIfBlankPreserve(request.DescriptionPt, current.DescriptionPt),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            UpdatedAt = _clock.UtcNow
        };

        var updated = await _statuses.UpdateAsync(dto, cancellationToken);
        return Result.Success(TournamentStatusMapper.ToResponse(updated));
    }

    private static string NonBlankOrFallback(string? incoming, string fallback)
        => string.IsNullOrWhiteSpace(incoming) ? fallback : incoming.Trim();

    private static string? NullIfBlankPreserve(string? incoming, string? fallback)
    {
        if (incoming is null) return fallback;
        return string.IsNullOrWhiteSpace(incoming) ? null : incoming.Trim();
    }
}
