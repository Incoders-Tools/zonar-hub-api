using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Players;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.EligibilityProfiles;

namespace ZonarHub.ApiService.Endpoints.Admin.Circuit.EligibilityProfiles;

/// <summary>
/// Eligibility profiles of an organization (D1, D3, D5): reusable participant slot rules a tournament may reference.
/// Same organization tenant rule as the player endpoints (403 <c>tournaments.forbidden</c>). The tournament's profile
/// (R2b) has its own route, so the tournament create and update contracts stay unchanged.
/// </summary>
public static class AdminEligibilityProfilesEndpointsExtensions
{
    private const string Tag = "Admin.EligibilityProfiles";
    private const string RoutePrefix = "/api/admin/organizations/{organizationId:guid}/eligibility-profiles";
    private const string TournamentRoute = "/api/admin/tournaments/{tournamentId:guid}/eligibility-profile";

    private const string TournamentAccessDescription =
        "404 tournaments.not_found for an unknown tournament; 403 tournaments.forbidden for a tournament outside the caller's tenant.";

    private const string BodyDescription =
        "Body { name, description?, isActive (update only), slots: [{ slotNumber 1..16, genderId?, categoryId?, minAge?, maxAge?, label?, required (default true) }] }. Null rules do not restrict; the mixed gender admits any player; a category allows players one level away. 400 eligibility_profiles.* (name_required, name_too_long, description_too_long, slots_required, invalid_slot, invalid_age_range, label_too_long, unknown_gender, unknown_category); 409 eligibility_profiles.name_taken when another profile of the organization has the name (any case).";

    public static IEndpointRouteBuilder MapAdminEligibilityProfilesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/", ListAsync)
            .WithName("ListAdminEligibilityProfiles")
            .WithSummary("List the organization's eligibility profiles")
            .WithDescription("Returns every profile with its slots in slot order, ordered by name.")
            .Produces<IReadOnlyList<EligibilityProfileResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAdminEligibilityProfile")
            .WithSummary("Get an eligibility profile")
            .WithDescription("404 eligibility_profiles.not_found for an unknown id or another organization's profile.")
            .Produces<EligibilityProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateAdminEligibilityProfile")
            .WithSummary("Create an eligibility profile")
            .WithDescription($"Creates an active profile. {BodyDescription}")
            .Produces<EligibilityProfileResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateAdminEligibilityProfile")
            .WithSummary("Update an eligibility profile")
            .WithDescription($"Replaces name, description, activity and every slot. 404 eligibility_profiles.not_found for an unknown id. {BodyDescription}")
            .Produces<EligibilityProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteAdminEligibilityProfile")
            .WithSummary("Delete an eligibility profile")
            .WithDescription("Deletes the profile with its slots. 404 eligibility_profiles.not_found for an unknown id; 409 eligibility_profiles.in_use while a tournament references it (nothing is deleted).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var tournament = app.MapGroup(TournamentRoute)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        tournament.MapGet("/", GetTournamentProfileAsync)
            .WithName("GetAdminTournamentEligibilityProfile")
            .WithSummary("Get the tournament's eligibility profile")
            .WithDescription($"Returns {{ eligibilityProfileId }}, null when the tournament has none. {TournamentAccessDescription}")
            .Produces<TournamentEligibilityProfileBody>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tournament.MapPut("/", SetTournamentProfileAsync)
            .WithName("SetAdminTournamentEligibilityProfile")
            .WithSummary("Assign an eligibility profile to the tournament")
            .WithDescription($"Body {{ eligibilityProfileId }}: a profile of the tournament's own organization, or null to clear it; returns the same body. An inactive profile may be assigned: while inactive it is ignored and the tournament's own category, gender and modality rules apply. {TournamentAccessDescription} 404 eligibility_profiles.not_found for an unknown profile or one of another organization; nothing is written.")
            .Produces<TournamentEligibilityProfileBody>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    internal static async Task<IResult> ListAsync(Guid organizationId, ISender sender, CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new ListEligibilityProfilesQuery(organizationId), cancellationToken), profiles => TypedResults.Ok(profiles));

    internal static async Task<IResult> GetAsync(Guid organizationId, Guid id, ISender sender, CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new GetEligibilityProfileQuery(organizationId, id), cancellationToken), profile => TypedResults.Ok(profile));

    internal static async Task<IResult> CreateAsync(
        Guid organizationId,
        [FromBody] EligibilityProfileBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new CreateEligibilityProfileCommand(organizationId, body), cancellationToken),
            profile => TypedResults.Created($"/api/admin/organizations/{organizationId}/eligibility-profiles/{profile.Id}", profile));

    internal static async Task<IResult> UpdateAsync(
        Guid organizationId,
        Guid id,
        [FromBody] EligibilityProfileBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new UpdateEligibilityProfileCommand(organizationId, id, body), cancellationToken),
            profile => TypedResults.Ok(profile));

    internal static async Task<IResult> GetTournamentProfileAsync(Guid tournamentId, ISender sender, CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new GetTournamentEligibilityProfileQuery(tournamentId), cancellationToken), assignment => TypedResults.Ok(assignment));

    internal static async Task<IResult> SetTournamentProfileAsync(
        Guid tournamentId,
        [FromBody] TournamentEligibilityProfileBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        AdminPlayersEndpointsExtensions.Map(
            await sender.Send(new SetTournamentEligibilityProfileCommand(tournamentId, body.EligibilityProfileId), cancellationToken),
            assignment => TypedResults.Ok(assignment));

    internal static async Task<IResult> DeleteAsync(Guid organizationId, Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteEligibilityProfileCommand(organizationId, id), cancellationToken);
        return AdminTournamentsEndpointsExtensions.IsForbidden(result.Error)
            ? AdminTournamentsEndpointsExtensions.Forbidden(result.Error)
            : result.Match(() => TypedResults.NoContent());
    }
}
