namespace ZonarHub.Application.Features.AdminDashboard;

public sealed record AdminDashboardSummaryResponse(
    Guid OrganizationId,
    int TotalTournaments,
    int ActiveTournaments,
    int FinishedTournaments,
    int TotalPlayers,
    int TotalRegistrations,
    int ComplexCount,
    int CourtCount,
    int AdminCount,
    int ActiveSportsCount);
