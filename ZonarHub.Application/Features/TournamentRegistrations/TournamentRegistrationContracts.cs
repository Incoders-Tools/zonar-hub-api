using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.Application.Features.TournamentRegistrations;

/// <summary>Body of <c>POST /api/admin/tournaments/{tournamentId}/registrations</c> (S4, D5).</summary>
/// <param name="Participants">One entry per filled slot (1-based) with a player of the tournament's organization.</param>
/// <param name="Status"><c>pending</c> (default; waitlisted by the store when the places are taken) or <c>confirmed</c>.</param>
/// <param name="ContactEmail">Optional contact email (stored lowercase).</param>
/// <param name="Notes">Optional organizer notes, up to 2000 characters.</param>
/// <param name="PublicNameConsent">True when the names may appear in the public confirmed pairs (D4).</param>
public sealed record CreateRegistrationBody(
    IReadOnlyList<RegistrationParticipantBody>? Participants,
    string? Status = null,
    string? ContactEmail = null,
    string? Notes = null,
    bool PublicNameConsent = false);

/// <summary>A player in one slot of a new registration.</summary>
public sealed record RegistrationParticipantBody(int SlotNumber, Guid PlayerId);

/// <summary>Optional body of the status endpoints.</summary>
/// <param name="Revision">The revision the client read; when given and no longer current the request fails with 409.</param>
public sealed record RegistrationRevisionBody(int? Revision = null);

/// <summary>Body of <c>PUT .../registrations/{registrationId}/payment</c>.</summary>
/// <param name="PaymentStatus"><c>paid</c> (from pending or failed) or <c>refunded</c> (from paid).</param>
/// <param name="Revision">As in <see cref="RegistrationRevisionBody"/>.</param>
public sealed record RegistrationPaymentBody(string? PaymentStatus, int? Revision = null);

/// <summary>A registration with its participants in slot order. Enum values are camelCase; timestamps are UTC.</summary>
/// <param name="Status"><c>pending</c>, <c>confirmed</c>, <c>rejected</c>, <c>waitlisted</c> or <c>cancelled</c>.</param>
/// <param name="Source"><c>public</c> or <c>admin</c>.</param>
/// <param name="PaymentStatus"><c>notRequired</c>, <c>pending</c>, <c>paid</c>, <c>refunded</c> or <c>failed</c>.</param>
/// <param name="Revision">Current revision; send it back to make a status or payment change fail when stale.</param>
public sealed record TournamentRegistrationResponse(
    Guid Id,
    Guid TournamentId,
    string Status,
    string Source,
    string PaymentStatus,
    string? ContactEmail,
    string? Notes,
    bool PublicNameConsent,
    IReadOnlyList<RegistrationParticipantResponse> Participants,
    int Revision,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>A participant of a registration.</summary>
/// <param name="DisplayName">First and last name separated by a space.</param>
public sealed record RegistrationParticipantResponse(
    int SlotNumber,
    Guid PlayerId,
    string FirstName,
    string LastName,
    string DisplayName,
    string? Club);

/// <summary>The tournament's registrations (optionally filtered) and its places, counted over all registrations.</summary>
public sealed record TournamentRegistrationListResponse(
    IReadOnlyList<TournamentRegistrationResponse> Items,
    RegistrationCapacityResponse Capacity);

/// <summary>Places of the tournament (D2): <c>maxPairs</c> limits confirmed registrations; null means no limit.</summary>
/// <param name="PlacesLeft">Places still free for confirmation; null without a limit.</param>
public sealed record RegistrationCapacityResponse(int? MaxPairs, int Confirmed, int Pending, int Waitlisted, int? PlacesLeft);

/// <summary>The camelCase API spelling of the registration enums; parsing is exact (ordinal, case-sensitive).</summary>
public static class RegistrationApiNames
{
    private static readonly Dictionary<RegistrationStatus, string> Statuses = new()
    {
        [RegistrationStatus.Pending] = "pending",
        [RegistrationStatus.Confirmed] = "confirmed",
        [RegistrationStatus.Rejected] = "rejected",
        [RegistrationStatus.Waitlisted] = "waitlisted",
        [RegistrationStatus.Cancelled] = "cancelled",
    };

    private static readonly Dictionary<RegistrationSource, string> Sources = new()
    {
        [RegistrationSource.Public] = "public",
        [RegistrationSource.Admin] = "admin",
    };

    private static readonly Dictionary<RegistrationPaymentStatus, string> PaymentStatuses = new()
    {
        [RegistrationPaymentStatus.NotRequired] = "notRequired",
        [RegistrationPaymentStatus.Pending] = "pending",
        [RegistrationPaymentStatus.Paid] = "paid",
        [RegistrationPaymentStatus.Refunded] = "refunded",
        [RegistrationPaymentStatus.Failed] = "failed",
    };

    public static string Name(RegistrationStatus value) => Statuses[value];

    public static string Name(RegistrationSource value) => Sources[value];

    public static string Name(RegistrationPaymentStatus value) => PaymentStatuses[value];

    public static bool TryParse(string? name, out RegistrationStatus value) => TryParse(Statuses, name, out value);

    public static bool TryParse(string? name, out RegistrationPaymentStatus value) => TryParse(PaymentStatuses, name, out value);

    private static bool TryParse<T>(Dictionary<T, string> names, string? name, out T value)
        where T : struct
    {
        foreach (var (candidate, wire) in names)
        {
            if (string.Equals(wire, name, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}

internal static class TournamentRegistrationMapper
{
    /// <summary>Joins the member names of a draw participant (D5).</summary>
    public const string MemberSeparator = " / ";

    public static TournamentRegistrationResponse ToResponse(TournamentRegistrationDetails details)
    {
        var r = details.Registration;
        return new TournamentRegistrationResponse(
            r.Id.Value,
            r.TournamentId.Value,
            RegistrationApiNames.Name(r.Status),
            RegistrationApiNames.Name(r.Source),
            RegistrationApiNames.Name(r.PaymentStatus),
            r.ContactEmail,
            r.Notes,
            r.PublicNameConsent,
            details.Participants
                .Select(p => new RegistrationParticipantResponse(p.SlotNumber, p.PlayerId.Value, p.FirstName, p.LastName, p.DisplayName, p.Club))
                .ToList(),
            r.Revision,
            r.CreatedAtUtc,
            r.UpdatedAtUtc);
    }

    /// <summary>
    /// D5: <c>id</c> = registration id, <c>members</c> in slot order, <c>displayName</c> = members joined with " / ",
    /// <c>seedRank</c> empty, <c>separationKey</c> = the club all members share, else the first member's club. Both cases
    /// are the first member's club (null when that member has none).
    /// </summary>
    public static DrawParticipantBody ToDrawParticipant(TournamentRegistrationDetails details)
    {
        var members = details.Participants.OrderBy(p => p.SlotNumber).ToList();
        var names = members.Select(p => p.DisplayName).ToList();
        return new DrawParticipantBody(
            details.Registration.Id.Value.ToString(),
            string.Join(MemberSeparator, names),
            names,
            null,
            members.FirstOrDefault()?.Club);
    }
}
