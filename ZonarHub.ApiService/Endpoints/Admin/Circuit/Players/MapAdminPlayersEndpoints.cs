using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.Players;
using ZonarHub.Domain.Common;

namespace ZonarHub.ApiService.Endpoints.Admin.Circuit.Players;

/// <summary>
/// Players of an organization (S3, D5). Every endpoint applies the organization tenant rule first: system administrators
/// may manage any organization, other callers only one of their own tenant; unknown or foreign organizations return 403
/// (<c>tournaments.forbidden</c>, the tenant code shared by the circuit endpoints).
/// </summary>
public static class AdminPlayersEndpointsExtensions
{
    private const string Tag = "Admin.Players";
    private const string RoutePrefix = "/api/admin/organizations/{organizationId:guid}/players";

    private const string BodyDescription =
        "Body { firstName, lastName, email?, phone?, documentId?, birthDate? (yyyy-MM-dd), genderId?, categoryId?, club?, isActive (update only) }. Text is trimmed, email stored lowercase. 400 players.* for invalid fields (first_name_required, last_name_required, name_too_long, invalid_email, field_too_long, birth_date_in_future, unknown_gender, unknown_category); 409 players.email_taken or players.document_taken when another player of the organization has it.";

    public static IEndpointRouteBuilder MapAdminPlayersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/", ListAsync)
            .WithName("ListAdminPlayers")
            .WithSummary("Search the organization's players")
            .WithDescription("Query search (contains on first name, last name, email or document, case-insensitive), isActive, page (default 1) and pageSize (default 20, max 200). Returns { items, page, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage } ordered by last name, first name and id.")
            .Produces<PageResult<PlayerResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAdminPlayer")
            .WithSummary("Get a player")
            .WithDescription("404 players.not_found for an unknown id or a player of another organization.")
            .Produces<PlayerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateAdminPlayer")
            .WithSummary("Create a player")
            .WithDescription($"Creates an active player without a user link. {BodyDescription}")
            .Produces<PlayerResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateAdminPlayer")
            .WithSummary("Update a player")
            .WithDescription($"Replaces every editable field and the activity; the user link is kept. Players are not deleted (their registrations keep them): isActive false retires one and new registrations refuse it. 404 players.not_found for an unknown id. {BodyDescription}")
            .Produces<PlayerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    internal static async Task<IResult> ListAsync(
        Guid organizationId,
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var query = new SearchPlayersQuery(
            organizationId, search, isActive, page ?? PageRequest.DefaultPage, pageSize ?? PageRequest.DefaultPageSize);
        return Map(await sender.Send(query, cancellationToken), players => TypedResults.Ok(players));
    }

    internal static async Task<IResult> GetAsync(Guid organizationId, Guid id, ISender sender, CancellationToken cancellationToken) =>
        Map(await sender.Send(new GetPlayerQuery(organizationId, id), cancellationToken), player => TypedResults.Ok(player));

    internal static async Task<IResult> CreateAsync(
        Guid organizationId,
        [FromBody] PlayerBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        Map(
            await sender.Send(new CreatePlayerCommand(organizationId, body), cancellationToken),
            player => TypedResults.Created($"/api/admin/organizations/{organizationId}/players/{player.Id}", player));

    internal static async Task<IResult> UpdateAsync(
        Guid organizationId,
        Guid id,
        [FromBody] PlayerBody body,
        ISender sender,
        CancellationToken cancellationToken) =>
        Map(await sender.Send(new UpdatePlayerCommand(organizationId, id, body), cancellationToken), player => TypedResults.Ok(player));

    /// <summary>The tenant refusal is a 403 (see <see cref="AdminTournamentsEndpointsExtensions.Forbidden"/>); other errors map by type.</summary>
    internal static IResult Map<T>(Result<T> result, Func<T, IResult> onSuccess) =>
        AdminTournamentsEndpointsExtensions.IsForbidden(result.Error)
            ? AdminTournamentsEndpointsExtensions.Forbidden(result.Error)
            : result.Match(onSuccess);
}
