using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments.Create;

public sealed record CreateTournamentCommand(
    Guid OrganizationId,
    string Name,
    string? Key,
    Guid? ComplexId,
    Guid SportId,
    Guid? CategoryId,
    Guid? GenderId,
    Guid? ModalityId,
    Guid? TournamentTypeId,
    Guid? RuleSetId,
    string? Status,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? RegistrationStartDate,
    DateOnly? RegistrationEndDate,
    int? MaxPairs,
    string? Description,
    string? Rules,
    string? ImageUrl,
    string? CoverImageUrl,
    decimal? RegistrationFeePerPair,
    decimal? PrizeMoney,
    int? PointsToAward,
    decimal? SumValue,
    string? Observations,
    bool IsActive,
    IReadOnlyList<Guid> SelectedCourtIds)
    : IRequest<Result<TournamentResponse>>;
