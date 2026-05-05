namespace ZonarHub.Application.Abstractions;

public interface ITournamentModalityRepository
{
    Task<IReadOnlyList<TournamentModalityDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);
}

public sealed record TournamentModalityDto(
    Guid Id,
    string NameEs,
    string NameEn,
    string NamePt,
    string Key,
    int SortOrder,
    bool IsActive);
