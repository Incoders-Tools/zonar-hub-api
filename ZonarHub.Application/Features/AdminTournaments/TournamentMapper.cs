using ZonarHub.Application.Abstractions;

namespace ZonarHub.Application.Features.AdminTournaments;

internal static class TournamentMapper
{
    public static TournamentResponse ToResponse(TournamentAdminDto dto) => new(
        dto.Id,
        dto.OrganizationId,
        dto.Name,
        dto.Key,
        dto.ComplexId,
        dto.SportId,
        dto.CategoryId,
        dto.GenderId,
        dto.ModalityId,
        dto.TournamentTypeId,
        dto.RuleSetId,
        dto.Status,
        dto.StartDate,
        dto.EndDate,
        dto.RegistrationStartDate,
        dto.RegistrationEndDate,
        dto.MaxPairs,
        dto.Description,
        dto.Rules,
        dto.ImageUrl,
        dto.CoverImageUrl,
        dto.RegistrationFeePerPair,
        dto.PrizeMoney,
        dto.PointsToAward,
        dto.SumValue,
        dto.Observations,
        dto.IsActive,
        dto.SelectedCourtIds,
        dto.CreatedAtUtc,
        dto.UpdatedAtUtc);
}
