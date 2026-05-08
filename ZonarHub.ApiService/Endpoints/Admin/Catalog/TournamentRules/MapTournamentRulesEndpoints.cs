using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.TournamentRules;
using ZonarHub.Application.Features.TournamentRules.Create;
using ZonarHub.Application.Features.TournamentRules.Delete;
using ZonarHub.Application.Features.TournamentRules.GetAll;
using ZonarHub.Application.Features.TournamentRules.GetById;
using ZonarHub.Application.Features.TournamentRules.Update;

namespace ZonarHub.ApiService.Endpoints.Admin.Catalog.TournamentRules;

public static class TournamentRulesEndpointsExtensions
{
    private const string Tag = "Admin.TournamentRules";
    private const string RoutePrefix = "/api/admin/tournament-rules";

    public static IEndpointRouteBuilder MapTournamentRulesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/", GetAllAsync)
            .WithName("GetAllTournamentRules")
            .WithSummary("List all tournament rule sets")
            .Produces<TournamentRuleListResponse>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetTournamentRuleById")
            .Produces<TournamentRuleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateTournamentRule")
            .Produces<TournamentRuleResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateTournamentRule")
            .Produces<TournamentRuleResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteTournamentRule")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = true)
    {
        var result = await sender.Send(new GetTournamentRulesQuery(includeInactive), cancellationToken);
        return result.Match(items => (IResult)TypedResults.Ok(new TournamentRuleListResponse(items)));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTournamentRuleByIdQuery(id), cancellationToken);
        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateTournamentRuleRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateTournamentRuleCommand(
                body.Name,
                body.DescriptionEs,
                body.DescriptionEn,
                body.DescriptionPt,
                body.SortOrder),
            cancellationToken);

        return result.Match(item => TypedResults.Created($"{RoutePrefix}/{item.Id}", item));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateTournamentRuleRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateTournamentRuleCommand(
                id,
                body.Name,
                body.DescriptionEs,
                body.DescriptionEn,
                body.DescriptionPt,
                body.SortOrder,
                body.IsActive),
            cancellationToken);

        return result.Match(item => (IResult)TypedResults.Ok(item));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteTournamentRuleCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record TournamentRuleListResponse(IReadOnlyList<TournamentRuleResponse> Items);

public sealed record CreateTournamentRuleRequest(
    string Name,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder);

public sealed record UpdateTournamentRuleRequest(
    string Name,
    string? DescriptionEs,
    string? DescriptionEn,
    string? DescriptionPt,
    int SortOrder,
    bool IsActive);
