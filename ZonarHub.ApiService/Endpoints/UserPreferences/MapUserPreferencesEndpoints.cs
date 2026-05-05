using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Features.UserPreferences;
using ZonarHub.Application.Features.UserPreferences.Get;
using ZonarHub.Application.Features.UserPreferences.Set;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ZonarHub.ApiService.Endpoints.UserPreferences;

public static class UserPreferencesEndpointsExtensions
{
    private const string Tag = "UserPreferences";

    public static IEndpointRouteBuilder MapUserPreferencesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/user-preferences")
            .WithTags(Tag);

        group.MapGet("/", GetUserPreferencesAsync)
            .WithName("GetUserPreferences")
            .WithSummary("Get all preferences for the authenticated user")
            .Produces<UserPreferencesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/", SetUserPreferenceAsync)
            .WithName("SetUserPreference")
            .WithSummary("Set or update a user preference")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetUserPreferencesAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] Guid? userId = null,
        [FromQuery] Guid? organizationId = null)
    {
        // TODO: Get userId from authentication context (ICurrentUser)
        // For now, accept from query parameter
        if (userId is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "User must be authenticated.");
        }

        var query = new GetUserPreferencesQuery(userId.Value, organizationId);
        var result = await sender.Send(query, cancellationToken);
        return result.Match(prefs => TypedResults.Ok(prefs));
    }

    private static async Task<IResult> SetUserPreferenceAsync(
        [FromBody] SetUserPreferenceRequest body,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] Guid? userId = null,
        [FromQuery] Guid? organizationId = null)
    {
        // TODO: Get userId from authentication context (ICurrentUser)
        // For now, accept from query parameter
        if (userId is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "User must be authenticated.");
        }

        var command = new SetUserPreferenceCommand(
            userId.Value,
            organizationId,
            body.Key,
            body.Value);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(() => TypedResults.NoContent());
    }
}

/// <summary>
/// Request body for setting a user preference.
/// </summary>
public sealed record SetUserPreferenceRequest(string Key, string Value);
