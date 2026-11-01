using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.AdminTournaments.Create;
using ZonarHub.Application.Features.AdminTournaments.Delete;
using ZonarHub.Application.Features.AdminTournaments.GetAll;
using ZonarHub.Application.Features.AdminTournaments.GetById;
using ZonarHub.Application.Features.AdminTournaments.Update;
using ZonarHub.Domain.Common;

namespace ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;

public static class AdminTournamentsEndpointsExtensions
{
    private const string Tag = "Admin.Tournaments";
    private const string RoutePrefix = "/api/admin/tournaments";

    public static IEndpointRouteBuilder MapAdminTournamentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAdminTournaments")
            .WithSummary("List tournaments for the active organization")
            .WithDescription("System administrators may list any organization; other callers only an organization of their own tenant. Unknown or foreign organizations return 403.")
            .Produces<TournamentListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetAdminTournamentById")
            .WithSummary("Get a tournament by id")
            .WithDescription("Unknown ids return 404. A tournament of an organization outside the caller's tenant returns 403; system administrators may read any tournament.")
            .Produces<TournamentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateAdminTournament")
            .WithSummary("Create a tournament")
            .WithDescription("System administrators may create in any organization; other callers only in an organization of their own tenant. Unknown or foreign organizations return 403. When isPublic is true the API generates the read-only publicSlug share link (<slugified-name>-<6 base36 chars>); clients cannot send a slug.")
            .Produces<TournamentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateAdminTournament")
            .WithSummary("Update a tournament")
            .WithDescription("The tournament keeps its organization. Unknown ids return 404; a tournament of an organization outside the caller's tenant returns 403 before validation. The first save with isPublic true generates the read-only publicSlug; it is never regenerated, so turning isPublic off and on again keeps the same share link.")
            .Produces<TournamentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteAdminTournament")
            .WithSummary("Delete a tournament")
            .WithDescription("Unknown ids return 404; a tournament of an organization outside the caller's tenant returns 403 and is not deleted.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    internal static async Task<IResult> GetAllAsync(
        [FromQuery] Guid organizationId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTournamentsQuery(organizationId), cancellationToken);
        return IsForbidden(result.Error)
            ? Forbidden(result.Error)
            : result.Match(items => (IResult)TypedResults.Ok(new TournamentListResponse(items)));
    }

    internal static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTournamentByIdQuery(id), cancellationToken);
        return IsForbidden(result.Error)
            ? Forbidden(result.Error)
            : result.Match(item => (IResult)TypedResults.Ok(item));
    }

    internal static async Task<IResult> CreateAsync(
        [FromBody] CreateTournamentRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateTournamentCommand(
            body.OrganizationId,
            body.Name,
            body.Key,
            body.ComplexId,
            body.SportId,
            body.CategoryId,
            body.GenderId,
            body.ModalityId,
            body.TournamentTypeId,
            body.RuleSetId,
            body.Status,
            body.StartDate,
            body.EndDate,
            body.RegistrationStartDate,
            body.RegistrationEndDate,
            body.MaxPairs,
            body.Description,
            body.Rules,
            body.ImageUrl,
            body.CoverImageUrl,
            body.RegistrationFeePerPair,
            body.PrizeMoney,
            body.PointsToAward,
            body.SumValue,
            body.Observations,
            body.IsActive,
            body.SelectedCourtIds ?? [],
            body.IsPublic);

        var result = await sender.Send(command, cancellationToken);
        return IsForbidden(result.Error)
            ? Forbidden(result.Error)
            : result.Match(item => TypedResults.Created($"{RoutePrefix}/{item.Id}", item));
    }

    internal static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateTournamentRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateTournamentCommand(
            id,
            body.Name,
            body.Key,
            body.ComplexId,
            body.SportId,
            body.CategoryId,
            body.GenderId,
            body.ModalityId,
            body.TournamentTypeId,
            body.RuleSetId,
            body.Status,
            body.StartDate,
            body.EndDate,
            body.RegistrationStartDate,
            body.RegistrationEndDate,
            body.MaxPairs,
            body.Description,
            body.Rules,
            body.ImageUrl,
            body.CoverImageUrl,
            body.RegistrationFeePerPair,
            body.PrizeMoney,
            body.PointsToAward,
            body.SumValue,
            body.Observations,
            body.IsActive,
            body.SelectedCourtIds ?? [],
            body.IsPublic);

        var result = await sender.Send(command, cancellationToken);
        return IsForbidden(result.Error)
            ? Forbidden(result.Error)
            : result.Match(item => (IResult)TypedResults.Ok(item));
    }

    internal static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteTournamentCommand(id), cancellationToken);
        return IsForbidden(result.Error)
            ? Forbidden(result.Error)
            : result.Match(() => (IResult)TypedResults.NoContent());
    }

    internal static bool IsForbidden(Error error) => error == AdminTournamentErrors.Forbidden;

    /// <summary>
    /// The shared error mapper has no 403 type, so the tenant refusal is mapped here for every tournament endpoint
    /// (including the tournament draw endpoints).
    /// </summary>
    internal static IResult Forbidden(Error error) => TypedResults.Problem(
        title: "Forbidden",
        detail: error.MessageKey,
        statusCode: StatusCodes.Status403Forbidden,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}

/// <summary>
/// Body of POST /api/admin/tournaments. <c>IsPublic</c> shows the tournament in ZonarHub public sections and
/// makes the API generate its share-link slug; the slug itself is read-only and never accepted from clients.
/// </summary>
public sealed record CreateTournamentRequest(
    Guid OrganizationId,
    string Name,
    string? Key,
    Guid? ComplexId,
    Guid SportId,
    Guid? CategoryId,
    Guid? GenderId,
    Guid? ModalityId,
    Guid? TournamentTypeId,
    Guid? RuleSetId,
    string? Status,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? RegistrationStartDate,
    DateOnly? RegistrationEndDate,
    int? MaxPairs,
    string? Description,
    string? Rules,
    string? ImageUrl,
    string? CoverImageUrl,
    decimal? RegistrationFeePerPair,
    decimal? PrizeMoney,
    int? PointsToAward,
    decimal? SumValue,
    string? Observations,
    bool IsActive,
    IReadOnlyList<Guid>? SelectedCourtIds,
    bool IsPublic = false);

/// <summary>
/// Body of PUT /api/admin/tournaments/{id}. <c>IsPublic</c> toggles public visibility; an existing share-link
/// slug is kept when it is turned off and reused when it is turned on again.
/// </summary>
public sealed record UpdateTournamentRequest(
    string Name,
    string? Key,
    Guid? ComplexId,
    Guid SportId,
    Guid? CategoryId,
    Guid? GenderId,
    Guid? ModalityId,
    Guid? TournamentTypeId,
    Guid? RuleSetId,
    string? Status,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? RegistrationStartDate,
    DateOnly? RegistrationEndDate,
    int? MaxPairs,
    string? Description,
    string? Rules,
    string? ImageUrl,
    string? CoverImageUrl,
    decimal? RegistrationFeePerPair,
    decimal? PrizeMoney,
    int? PointsToAward,
    decimal? SumValue,
    string? Observations,
    bool IsActive,
    IReadOnlyList<Guid>? SelectedCourtIds,
    bool IsPublic = false);
