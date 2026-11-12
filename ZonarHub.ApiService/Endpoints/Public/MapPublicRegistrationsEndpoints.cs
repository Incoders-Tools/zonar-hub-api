using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.ApiService.Security;
using ZonarHub.Application.Features.PublicRegistrations;
using ZonarHub.Domain.Common;

namespace ZonarHub.ApiService.Endpoints.Public;

/// <summary>
/// Anonymous registration of a public tournament by its share slug (S2, S4, D4). The group is explicitly opened
/// (<c>AllowAnonymous</c>). Tournaments the public page does not show (not public, inactive, cancelled, unknown or
/// malformed slug) are 404 <c>public_tournaments.not_found</c> on every route. No response carries player or
/// organization ids, emails, phones or documents.
/// </summary>
public static class PublicRegistrationsEndpointsExtensions
{
    private const string Tag = "Public.Registrations";
    private const string RoutePrefix = "/api/public/tournaments/{slug}";

    /// <summary>Places change with every registration, so the form data is cached briefly.</summary>
    private const string InfoCacheControl = "public, max-age=15";

    private const string ConfirmedPairsCacheControl = "public, max-age=60";

    public static IEndpointRouteBuilder MapPublicRegistrationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .AllowAnonymous();

        group.MapGet("/registration", GetInfoAsync)
            .WithName("GetPublicTournamentRegistration")
            .WithSummary("Get the registration form data of a public tournament")
            .WithDescription("Anonymous. Returns { slug, state open|notStarted|ended, isOpen, opensOn, closesOn, capacity: { maxPairs, placesLeft, waitlistOnly }, fee: { amountPerPair, required }, slots: [{ slotNumber, label, required, genderId, categoryId, minAge, maxAge }], categoryTolerance, consentTextKey }. The window is registrationStartDate..registrationEndDate inclusive, today in UTC; a missing date does not restrict. placesLeft counts confirmed and pending registrations; waitlistOnly means a new registration is waitlisted. Slots come from the tournament's active eligibility profile, else from its modality, gender and category (an inactive profile is ignored); genderId and categoryId are ids of the anonymous catalogs /api/catalog/genders and /api/catalog/categories, null means no rule, the mixed gender admits any. 404 eligibility_profiles.not_found when the referenced profile is not the organization's. Cacheable for 15 seconds.")
            .Produces<PublicRegistrationInfoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/registration/code", SendCodeAsync)
            .WithName("SendPublicTournamentRegistrationCode")
            .WithSummary("Email a registration code")
            .WithDescription("Anonymous. Body { contactEmail }. Sends a 6-digit code valid for 15 minutes, only for this tournament and address (not the sign-up code); a new code replaces the previous one. Returns 202 { expiresInSeconds, resendAfterSeconds }. 400 registrations.contact_email_required or registrations.invalid_contact_email; 409 public_registrations.closed outside the registration window; 429 public_registrations.code_cooldown when a code was sent to the same address for this tournament less than 60 seconds ago. Rate limited per caller (policy public-registration-code, Security:PublicRegistrationCodePermitLimit per Security:WindowSeconds): 429 without body when exceeded.")
            .Produces<PublicRegistrationCodeResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(ApiRateLimitPolicies.PublicRegistrationCode);

        group.MapPost("/registration", SubmitAsync)
            .WithName("SubmitPublicTournamentRegistration")
            .WithSummary("Register a pair with the emailed code")
            .WithDescription("Anonymous. Body { contactEmail, code, participants: [{ slotNumber, firstName, lastName, email?, phone?, documentId?, birthDate?, genderId?, categoryId?, club? }], publicNameConsent }. Each participant is the organization's player with that email (any case), else with that document, else a new player; an existing player is reused as stored and never written from the public form, and its stored data is what the eligibility rules read. Returns 201 { reference, status pending|waitlisted, payment: { status notRequired|pending, amount } } (waitlisted when confirmed plus pending registrations reach maxPairs); the organizer confirms or rejects it. Nothing is written on an error and the code stays usable: 400 public_registrations.invalid_code (missing, wrong, expired, used, of another address or tournament; 5 wrong attempts remove the code), 400 players.* and registrations.* for invalid typed participants, duplicate_player, slot errors and eligibility errors of new participants (gender_mismatch, category_out_of_range, age_out_of_range; missing data fails the rule that needs it), 404 eligibility_profiles.not_found, 409 public_registrations.closed, 409 public_registrations.participant_unavailable whenever a participant matches a stored player who cannot join (any cause, never detailed). Rate limited per caller (policy anonymous-auth).")
            .Produces<PublicRegistrationSubmittedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(ApiRateLimitPolicies.AnonymousAuth);

        group.MapGet("/confirmed-pairs", GetConfirmedPairsAsync)
            .WithName("GetPublicTournamentConfirmedPairs")
            .WithSummary("List the confirmed pairs of a public tournament")
            .WithDescription("Anonymous. Returns { items: [{ position, displayName, members, labelKey }] } in registration order. Registrations whose participants consented carry displayName (members joined with \" / \") and members (names in slot order); the others carry only labelKey public_registrations.confirmed_pairs.anonymous. Cacheable for 60 seconds.")
            .Produces<PublicConfirmedPairsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    internal static async Task<IResult> GetInfoAsync(string slug, HttpContext httpContext, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPublicRegistrationInfoQuery(slug), cancellationToken);
        return result.Match(info => Cached(httpContext, TypedResults.Ok(info), InfoCacheControl));
    }

    internal static async Task<IResult> SendCodeAsync(
        string slug,
        [FromBody] PublicRegistrationCodeBody body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SendPublicRegistrationCodeCommand(slug, body), cancellationToken);
        if (result.IsFailure && result.Error == PublicRegistrationErrors.CodeCooldown)
            return TooManyRequests(result.Error);

        return result.Match(sent => TypedResults.Accepted((string?)null, sent));
    }

    internal static async Task<IResult> SubmitAsync(
        string slug,
        [FromBody] PublicRegistrationBody body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SubmitPublicRegistrationCommand(slug, body), cancellationToken);
        return result.Match(submitted => TypedResults.Created((string?)null, submitted));
    }

    internal static async Task<IResult> GetConfirmedPairsAsync(string slug, HttpContext httpContext, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPublicConfirmedPairsQuery(slug), cancellationToken);
        return result.Match(pairs => Cached(httpContext, TypedResults.Ok(pairs), ConfirmedPairsCacheControl));
    }

    /// <summary>The resend wait is a throttle, so it is answered as 429 with the same problem shape as other errors.</summary>
    private static IResult TooManyRequests(Error error) => TypedResults.Problem(
        title: "Too many requests",
        detail: error.MessageKey,
        statusCode: StatusCodes.Status429TooManyRequests,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    private static IResult Cached(HttpContext httpContext, IResult result, string cacheControl)
    {
        httpContext.Response.Headers.CacheControl = cacheControl;
        return result;
    }
}
