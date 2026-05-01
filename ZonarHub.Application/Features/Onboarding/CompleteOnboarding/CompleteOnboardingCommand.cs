using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using MediatR;

namespace ZonarHub.Application.Features.Onboarding.CompleteOnboarding;

/// <summary>
/// Persists all data collected during the first-run onboarding wizard in a single atomic operation.
/// </summary>
public sealed record CompleteOnboardingCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    // Step 1 — Organization
    string OrganizationDisplayName,
    OrganizationType OrganizationType,
    // Step 2 — System settings (optional; only non-null values are persisted)
    OnboardingSystemSettingsInput? SystemSettings,
    // Step 3 — Venue + courts (optional)
    OnboardingVenueInput? Venue,
    // Step 4 — Sports (at least one required)
    IEnumerable<Guid> EnabledSportIds,
    // Step 5 — Tournament (optional)
    OnboardingTournamentInput? Tournament
) : IRequest<Result<CompleteOnboardingResponse>>;

public sealed record OnboardingSystemSettingsInput(
    string? Locale,
    string? Theme,
    string? Timezone,
    string? DateFormat);

public sealed record OnboardingVenueInput(
    string Name,
    string Address,
    string? Location,
    IEnumerable<string> CourtNames);

public sealed record OnboardingTournamentInput(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);
