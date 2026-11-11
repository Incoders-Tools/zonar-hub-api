using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.PublicRegistrations;

/// <summary>
/// Errors of the public registration flow. A tournament outside the showcase is
/// <see cref="PublicTournaments.PublicTournamentErrors.NotFound"/>; input errors reuse the <c>players.*</c> and
/// <c>registrations.*</c> codes.
/// </summary>
public static class PublicRegistrationErrors
{
    /// <summary>Today (UTC) is outside the registration window.</summary>
    public static readonly Error Closed = Error.Conflict("public_registrations.closed", "public_registrations.errors.closed");

    /// <summary>Missing, wrong, expired, already used or exhausted code; also a code of another email or tournament.</summary>
    public static readonly Error InvalidCode = Error.Validation("public_registrations.invalid_code", "public_registrations.errors.invalid_code");

    /// <summary>A code for this address and tournament was sent less than the resend wait ago (HTTP 429).</summary>
    public static readonly Error CodeCooldown = Error.Conflict("public_registrations.code_cooldown", "public_registrations.errors.code_cooldown");

    /// <summary>The participant's email and document belong to two different players of the organization.</summary>
    public static readonly Error ParticipantConflict = Error.Conflict("public_registrations.participant_conflict", "public_registrations.errors.participant_conflict");

    /// <summary>The participant matches a player the organization deactivated.</summary>
    public static readonly Error ParticipantUnavailable = Error.Conflict("public_registrations.participant_unavailable", "public_registrations.errors.participant_unavailable");
}
