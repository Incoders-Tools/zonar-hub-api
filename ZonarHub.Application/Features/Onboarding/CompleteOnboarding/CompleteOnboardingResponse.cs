namespace ZonarHub.Application.Features.Onboarding.CompleteOnboarding;

public sealed record CompleteOnboardingResponse(
    Guid OrganizationId,
    Guid? ComplexId,
    IReadOnlyList<Guid> CourtIds,
    IReadOnlyList<Guid> EnabledSportIds,
    Guid? TournamentId);
