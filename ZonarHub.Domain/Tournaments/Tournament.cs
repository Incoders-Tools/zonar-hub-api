using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Domain.Tournaments;

/// <summary>
/// Torneo organizado por una organización.
/// </summary>
public sealed class Tournament : Entity<TournamentId>
{
    private Tournament(TournamentId id) : base(id) { }

    public OrganizationId OrganizationId { get; private set; }
    public ComplexId? ComplexId { get; private set; }
    public SportId SportId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public TournamentStatus Status { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Tournament Reconstitute(
        TournamentId id,
        OrganizationId organizationId,
        ComplexId? complexId,
        SportId sportId,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        TournamentStatus status,
        bool isActive,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new Tournament(id)
        {
            OrganizationId = organizationId,
            ComplexId = complexId,
            SportId = sportId,
            Name = name,
            StartDate = startDate,
            EndDate = endDate,
            Status = status,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
        };
    }

    public static Result<Tournament> Create(
        TournamentId id,
        OrganizationId organizationId,
        ComplexId? complexId,
        SportId sportId,
        string name,
        DateOnly startDate,
        DateOnly endDate,
        DateTime nowUtc)
    {
        if (organizationId.Value == Guid.Empty)
            return Result.Failure<Tournament>(TournamentErrors.OrganizationIdRequired);

        if (sportId.Value == Guid.Empty)
            return Result.Failure<Tournament>(TournamentErrors.SportIdRequired);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Tournament>(TournamentErrors.NameRequired);

        if (endDate < startDate)
            return Result.Failure<Tournament>(TournamentErrors.InvalidDateRange);

        return Result.Success(new Tournament(id)
        {
            OrganizationId = organizationId,
            ComplexId = complexId,
            SportId = sportId,
            Name = name.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            Status = TournamentStatus.Upcoming,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }
}
