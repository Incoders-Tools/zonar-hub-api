using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Players;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Application.Features.TournamentRegistrations;
using ZonarHub.Domain.Registrations;

namespace ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;

/// <summary>
/// Admin registrations of a tournament (S4, D2, D3, D5). Every endpoint applies the tournament tenant rule first:
/// unknown tournaments return 404 (<c>tournaments.not_found</c>), tournaments outside the caller's tenant 403
/// (<c>tournaments.forbidden</c>).
/// </summary>
public static class AdminTournamentRegistrationsEndpointsExtensions
{
    private const string Tag = "Admin.TournamentRegistrations";
    private const string RoutePrefix = "/api/admin/tournaments/{tournamentId:guid}/registrations";

    private const string RevisionDescription =
        "Optional body { revision }: when given and no longer current, 409 registrations.concurrent_modification without writing. A change committed by someone else between the read and the write is also 409 registrations.concurrent_modification: nothing was written; read the registration again and retry.";

    public static IEndpointRouteBuilder MapAdminTournamentRegistrationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/", ListAsync)
            .WithName("ListAdminTournamentRegistrations")
            .WithSummary("List the tournament's registrations")
            .WithDescription("Returns { items, capacity: { maxPairs, confirmed, pending, waitlisted, placesLeft } } in creation order. Optional query status pending|confirmed|rejected|waitlisted|cancelled filters the items; capacity always counts every registration. 400 registrations.invalid_status for another status.")
            .Produces<TournamentRegistrationListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{registrationId:guid}", GetAsync)
            .WithName("GetAdminTournamentRegistration")
            .WithSummary("Get a registration")
            .WithDescription("404 registrations.not_found for an unknown id or a registration of another tournament. Send its revision with the status and payment requests.")
            .Produces<TournamentRegistrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateAdminTournamentRegistration")
            .WithSummary("Register players in the tournament")
            .WithDescription("Body { participants: [{ slotNumber, playerId }], status pending (default)|confirmed, contactEmail?, notes?, publicNameConsent }. Players must be active players of the tournament's organization (404 registrations.player_not_found). Eligibility (D3) uses the tournament's eligibility profile slots, else its modality (single 1, doubles or none 2, teams up to 16) with its gender (mixed admits any) and category (one level of tolerance): 400 registrations.invalid_slot, required_slot_missing, gender_mismatch, category_out_of_range, age_out_of_range; other 400 registrations.* for invalid input. A pending registration becomes waitlisted when confirmed plus pending reach maxPairs. paymentStatus starts pending when the tournament has a fee, else notRequired. 409 registrations.player_already_registered, registrations.capacity_reached (confirmed without a place).")
            .Produces<TournamentRegistrationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        MapTransition(group, "confirm", "Confirm", "Confirm a pending or waitlisted registration", "409 registrations.capacity_reached when confirmed registrations already reach maxPairs. ");
        MapTransition(group, "reject", "Reject", "Reject a pending registration", string.Empty);
        MapTransition(group, "waitlist", "Waitlist", "Move a pending registration to the waiting list", string.Empty);
        MapTransition(group, "cancel", "Cancel", "Cancel a pending, waitlisted or confirmed registration", string.Empty);

        group.MapPut("/{registrationId:guid}/payment", SetPaymentAsync)
            .WithName("SetAdminTournamentRegistrationPayment")
            .WithSummary("Mark a manual payment")
            .WithDescription($"Body {{ paymentStatus: paid|refunded, revision? }} for cash payments: paid from pending or failed, refunded from paid, in any registration status. 400 registrations.invalid_payment_status for another value; 409 registrations.invalid_payment_transition from another payment status; 404 registrations.not_found. {RevisionDescription}")
            .Produces<TournamentRegistrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/draw-participants", DrawParticipantsAsync)
            .WithName("ListAdminTournamentRegistrationDrawParticipants")
            .WithSummary("Confirmed registrations as draw participants")
            .WithDescription("Returns [{ id, displayName, members, seedRank, separationKey }] in creation order, ready for the draw request participants: id is the registration id, members the player names in slot order, displayName the members joined with \" / \", seedRank null, separationKey the club all members share, else the first member's club.")
            .Produces<IReadOnlyList<DrawParticipantBody>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static void MapTransition(RouteGroupBuilder group, string action, string name, string summary, string detail)
    {
        Delegate handler = action switch
        {
            "confirm" => ConfirmAsync,
            "reject" => RejectAsync,
            "waitlist" => WaitlistAsync,
            _ => CancelAsync,
        };
        group.MapPost($"/{{registrationId:guid}}/{action}", handler)
            .WithName($"{name}AdminTournamentRegistration")
            .WithSummary(summary)
            .WithDescription($"Returns the registration at its next revision. 409 registrations.invalid_transition outside pending → confirmed|rejected|waitlisted|cancelled, waitlisted → confirmed|cancelled, confirmed → cancelled. {detail}404 registrations.not_found. {RevisionDescription}")
            .Produces<TournamentRegistrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    internal static async Task<IResult> ListAsync(
        Guid tournamentId,
        [FromQuery] string? status,
        ISender sender,
        CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new ListTournamentRegistrationsQuery(tournamentId, status), cancellationToken), list => TypedResults.Ok(list));

    internal static async Task<IResult> GetAsync(Guid tournamentId, Guid registrationId, ISender sender, CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new GetTournamentRegistrationQuery(tournamentId, registrationId), cancellationToken), r => TypedResults.Ok(r));

    internal static async Task<IResult> CreateAsync(
        Guid tournamentId,
        [FromBody] CreateRegistrationBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new CreateTournamentRegistrationCommand(tournamentId, body), cancellationToken),
            r => TypedResults.Created($"/api/admin/tournaments/{tournamentId}/registrations/{r.Id}", r));

    internal static Task<IResult> ConfirmAsync(
        Guid tournamentId,
        Guid registrationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RegistrationRevisionBody? body,
        ISender sender,
        CancellationToken cancellationToken) =>
        TransitionAsync(tournamentId, registrationId, RegistrationStatus.Confirmed, body, sender, cancellationToken);

    internal static Task<IResult> RejectAsync(
        Guid tournamentId,
        Guid registrationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RegistrationRevisionBody? body,
        ISender sender,
        CancellationToken cancellationToken) =>
        TransitionAsync(tournamentId, registrationId, RegistrationStatus.Rejected, body, sender, cancellationToken);

    internal static Task<IResult> WaitlistAsync(
        Guid tournamentId,
        Guid registrationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RegistrationRevisionBody? body,
        ISender sender,
        CancellationToken cancellationToken) =>
        TransitionAsync(tournamentId, registrationId, RegistrationStatus.Waitlisted, body, sender, cancellationToken);

    internal static Task<IResult> CancelAsync(
        Guid tournamentId,
        Guid registrationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RegistrationRevisionBody? body,
        ISender sender,
        CancellationToken cancellationToken) =>
        TransitionAsync(tournamentId, registrationId, RegistrationStatus.Cancelled, body, sender, cancellationToken);

    internal static async Task<IResult> SetPaymentAsync(
        Guid tournamentId,
        Guid registrationId,
        [FromBody] RegistrationPaymentBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(
                new SetTournamentRegistrationPaymentCommand(tournamentId, registrationId, body.PaymentStatus, body.Revision), cancellationToken),
            r => TypedResults.Ok(r));

    internal static async Task<IResult> DrawParticipantsAsync(Guid tournamentId, ISender sender, CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new ListRegistrationDrawParticipantsQuery(tournamentId), cancellationToken), list => TypedResults.Ok(list));

    private static async Task<IResult> TransitionAsync(
        Guid tournamentId,
        Guid registrationId,
        RegistrationStatus target,
        RegistrationRevisionBody? body,
        ISender sender,
        CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(
                new TransitionTournamentRegistrationCommand(tournamentId, registrationId, target, body?.Revision), cancellationToken),
            r => TypedResults.Ok(r));
}
