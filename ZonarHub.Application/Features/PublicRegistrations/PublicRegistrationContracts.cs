namespace ZonarHub.Application.Features.PublicRegistrations;

/// <summary>Body of <c>POST /api/public/tournaments/{slug}/registration/code</c> (S2, D4).</summary>
/// <param name="ContactEmail">Address that receives the code (stored lowercase); the same one must submit the registration.</param>
public sealed record PublicRegistrationCodeBody(string? ContactEmail);

/// <summary>The code was sent.</summary>
/// <param name="ExpiresInSeconds">Lifetime of the code.</param>
/// <param name="ResendAfterSeconds">Wait before another code may be requested for the same address and tournament.</param>
public sealed record PublicRegistrationCodeResponse(int ExpiresInSeconds, int ResendAfterSeconds);

/// <summary>Body of <c>POST /api/public/tournaments/{slug}/registration</c> (S2, S4, D4).</summary>
/// <param name="ContactEmail">The address the code was sent to.</param>
/// <param name="Code">The 6-digit code of that email.</param>
/// <param name="Participants">One entry per filled slot (1-based) of the tournament's slots.</param>
/// <param name="PublicNameConsent">True when the names may appear in the public confirmed pairs.</param>
public sealed record PublicRegistrationBody(
    string? ContactEmail,
    string? Code,
    IReadOnlyList<PublicParticipantBody>? Participants,
    bool PublicNameConsent = false);

/// <summary>
/// A participant as typed on the public form. A player of the organization with the same email (any case) or document
/// is reused and only its empty fields are completed; otherwise a new player is created with this data.
/// </summary>
/// <param name="GenderId">Optional id of the <c>genders</c> catalog (<c>GET /api/catalog/genders</c>).</param>
/// <param name="CategoryId">Optional id of the <c>categories</c> catalog (<c>GET /api/catalog/categories</c>).</param>
public sealed record PublicParticipantBody(
    int SlotNumber,
    string? FirstName,
    string? LastName,
    string? Email = null,
    string? Phone = null,
    string? DocumentId = null,
    DateOnly? BirthDate = null,
    Guid? GenderId = null,
    Guid? CategoryId = null,
    string? Club = null);

/// <summary>The registration was received.</summary>
/// <param name="Reference">Public reference of the registration, to quote to the organizer.</param>
/// <param name="Status"><c>pending</c>, or <c>waitlisted</c> when confirmed plus pending registrations already reach the places.</param>
/// <param name="Payment">Payment state; online payment fields are added here when available, without changing the others.</param>
public sealed record PublicRegistrationSubmittedResponse(
    string Reference,
    string Status,
    PublicRegistrationPaymentResponse Payment);

/// <param name="Status"><c>notRequired</c> (no fee) or <c>pending</c>.</param>
/// <param name="Amount">Fee per pair; null without a fee.</param>
public sealed record PublicRegistrationPaymentResponse(string Status, decimal? Amount);

/// <summary>What the public registration form needs (D4). Dates are calendar days; today is taken in UTC.</summary>
/// <param name="State"><c>open</c>, <c>notStarted</c> or <c>ended</c>.</param>
/// <param name="IsOpen">True when <paramref name="State"/> is <c>open</c>.</param>
/// <param name="OpensOn">First day of the window; null when it does not restrict.</param>
/// <param name="ClosesOn">Last day of the window; null when it does not restrict.</param>
/// <param name="Slots">The slots every registration must fill, in slot order.</param>
/// <param name="CategoryTolerance">Levels a player's category may differ from a slot's category.</param>
/// <param name="ConsentTextKey">Translation key of the consent text for <c>publicNameConsent</c>.</param>
public sealed record PublicRegistrationInfoResponse(
    string Slug,
    string State,
    bool IsOpen,
    DateOnly? OpensOn,
    DateOnly? ClosesOn,
    PublicRegistrationCapacityResponse Capacity,
    PublicRegistrationFeeResponse Fee,
    IReadOnlyList<PublicRegistrationSlotResponse> Slots,
    int CategoryTolerance,
    string ConsentTextKey);

/// <param name="MaxPairs">Places of the tournament; null without a limit.</param>
/// <param name="PlacesLeft">Places not taken by confirmed or pending registrations; null without a limit.</param>
/// <param name="WaitlistOnly">True when a new registration goes to the waiting list.</param>
public sealed record PublicRegistrationCapacityResponse(int? MaxPairs, int? PlacesLeft, bool WaitlistOnly);

/// <param name="AmountPerPair">Fee per pair as configured; null without a fee.</param>
/// <param name="Required">True when the fee is positive.</param>
public sealed record PublicRegistrationFeeResponse(decimal? AmountPerPair, bool Required);

/// <summary>Rules of one slot (D3). Null rules do not restrict; the <c>mixed</c> gender admits any gender.</summary>
/// <param name="Required">False when the slot may stay empty.</param>
/// <param name="MinAge">Whole years on today (UTC) from the birth date.</param>
public sealed record PublicRegistrationSlotResponse(
    int SlotNumber,
    string? Label,
    bool Required,
    Guid? GenderId,
    Guid? CategoryId,
    int? MinAge,
    int? MaxAge);

/// <summary>The confirmed registrations of the tournament, in registration order.</summary>
public sealed record PublicConfirmedPairsResponse(IReadOnlyList<PublicConfirmedPairResponse> Items);

/// <summary>
/// A confirmed registration. With name consent it carries the member names; otherwise only <paramref name="LabelKey"/>,
/// the translation key of an anonymous label.
/// </summary>
/// <param name="Position">1-based position in the list.</param>
/// <param name="DisplayName">Members joined with " / "; null without consent.</param>
/// <param name="Members">Member names in slot order; empty without consent.</param>
/// <param name="LabelKey">Null with consent.</param>
public sealed record PublicConfirmedPairResponse(
    int Position,
    string? DisplayName,
    IReadOnlyList<string> Members,
    string? LabelKey);

/// <summary>Stable keys of the public registration texts.</summary>
public static class PublicRegistrationKeys
{
    /// <summary>Consent text shown next to <c>publicNameConsent</c>.</summary>
    public const string ConsentText = "public_registrations.consent.public_name";

    /// <summary>Label of a confirmed registration without name consent.</summary>
    public const string AnonymousPair = "public_registrations.confirmed_pairs.anonymous";

    /// <summary>Email template of the registration code.</summary>
    public const string CodeEmailTemplate = "public_registrations.verification_code";
}
