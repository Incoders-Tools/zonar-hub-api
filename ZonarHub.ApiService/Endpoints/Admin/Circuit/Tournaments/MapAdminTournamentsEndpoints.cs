using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.AdminTournaments.Create;
using ZonarHub.Application.Features.AdminTournaments.Delete;
using ZonarHub.Application.Features.AdminTournaments.GetAll;
using ZonarHub.Application.Features.AdminTournaments.GetById;
using ZonarHub.Application.Features.AdminTournaments.Update;

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
            .Produces<TournamentListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetAdminTournamentById")
            .WithSummary("Get a tournament by id")
            .Produces<TournamentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateAdminTournament")
            .WithSummary("Create a tournament")
            .Produces<TournamentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateAdminTournament")
            .WithSummary("Update a tournament")
            .Produces<TournamentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteAdminTournament")
            .WithSummary("Delete a tournament")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        [FromQuery] Guid organizationId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTournamentsQuery(organizationId), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(new TournamentListResponse(items)));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTournamentByIdQuery(id), cancellationToken);
        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> CreateAsync(
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
            body.SelectedCourtIds ?? []);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(item => TypedResults.Created($"{RoutePrefix}/{item.Id}", item));
    }

    private static async Task<IResult> UpdateAsync(
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
            body.SelectedCourtIds ?? []);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteTournamentCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

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
    IReadOnlyList<Guid>? SelectedCourtIds);

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
    IReadOnlyList<Guid>? SelectedCourtIds);
