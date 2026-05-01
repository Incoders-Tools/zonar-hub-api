using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.SocialNetworks;
using ZonarHub.Application.Features.SocialNetworks.Create;
using ZonarHub.Application.Features.SocialNetworks.Delete;
using ZonarHub.Application.Features.SocialNetworks.GetAll;
using ZonarHub.Application.Features.SocialNetworks.GetById;
using ZonarHub.Application.Features.SocialNetworks.Update;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ZonarHub.ApiService.Endpoints.Admin.SocialNetworks;

public static class SocialNetworksEndpointsExtensions
{
    private const string Tag = "Admin.SocialNetworks";
    private const string RoutePrefix = "/api/admin/social-networks";

    public static IEndpointRouteBuilder MapSocialNetworksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix).WithTags(Tag);

        group.MapGet("/", ListAsync)
            .WithName("ListSocialNetworks")
            .WithSummary("List all social networks")
            .Produces<PageResult<SocialNetworkResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetSocialNetworkById")
            .WithSummary("Get a social network by id")
            .Produces<SocialNetworkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateSocialNetwork")
            .WithSummary("Create a new social network")
            .Produces<SocialNetworkResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        // TODO: .RequireAuthorization("Admin")

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSocialNetwork")
            .WithSummary("Update a social network")
            .Produces<SocialNetworkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        // TODO: .RequireAuthorization("Admin")

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSocialNetwork")
            .WithSummary("Delete a social network")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        // TODO: .RequireAuthorization("Admin")

        return app;
    }

    private static async Task<IResult> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? name = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var filter = new SocialNetworkFilter(name, isActive, page, pageSize);
        var result = await sender.Send(new GetSocialNetworksQuery(filter), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSocialNetworkByIdQuery(id), cancellationToken);
        return result.Match(n => (IResult)TypedResults.Ok(n));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateSocialNetworkRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateSocialNetworkCommand(
            body.Name,
            body.Key,
            body.Url,
            body.Description,
            body.FaIcon,
            body.SortOrder);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(n => TypedResults.Created($"{RoutePrefix}/{n.Id}", n));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateSocialNetworkRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateSocialNetworkCommand(
            id,
            body.Name,
            body.Url,
            body.Description,
            body.FaIcon,
            body.SortOrder,
            body.IsActive);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(n => (IResult)TypedResults.Ok(n));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteSocialNetworkCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record CreateSocialNetworkRequest(
    string Name,
    string Key,
    string? Url,
    string? Description,
    string? FaIcon,
    int SortOrder);

public sealed record UpdateSocialNetworkRequest(
    string Name,
    string? Url,
    string? Description,
    string? FaIcon,
    int SortOrder,
    bool IsActive);
