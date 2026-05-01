using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.SystemSettings;
using ZonarHub.Application.Features.SystemSettings.Create;
using ZonarHub.Application.Features.SystemSettings.Delete;
using ZonarHub.Application.Features.SystemSettings.GetAll;
using ZonarHub.Application.Features.SystemSettings.GetById;
using ZonarHub.Application.Features.SystemSettings.Update;
using ZonarHub.Domain.SystemSettings;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace ZonarHub.ApiService.Endpoints.SystemSettings;

public static class SystemSettingsEndpointsExtensions
{
    private const string Tag = "SystemSettings";

    public static IEndpointRouteBuilder MapSystemSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system-settings")
            .WithTags(Tag);

        group.MapPost("/", CreateAsync)
            .WithName("CreateSystemSetting")
            .WithSummary("Create a system/profile setting")
            .Produces<SystemSettingResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetSystemSettingById")
            .WithSummary("Get a system setting by id")
            .Produces<SystemSettingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListAsync)
            .WithName("GetSystemSettings")
            .WithSummary("List system settings with filters and pagination")
            .Produces<PageResult<SystemSettingResponse>>(StatusCodes.Status200OK);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSystemSetting")
            .WithSummary("Update a system setting")
            .Produces<SystemSettingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSystemSetting")
            .WithSummary("Delete a system setting")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateSystemSettingRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = body.TenantId;

        var command = new CreateSystemSettingCommand(body.Key, body.Value, body.Scope, tenantId, body.UserId);
        var result = await sender.Send(command, cancellationToken);
        return result.Match(setting => TypedResults.Created($"/api/system-settings/{setting.Id}", setting));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        Guid? requiredTenantId = null;
        var result = await sender.Send(new GetSystemSettingByIdQuery(id, requiredTenantId), cancellationToken);
        return result.Match(setting => (IResult)TypedResults.Ok(setting));
    }

    private static async Task<IResult> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] SystemSettingScope? scope = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? key = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {

        var filter = new SystemSettingsFilter(scope, tenantId, userId, key, page, pageSize);
        var result = await sender.Send(new GetSystemSettingsQuery(filter), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateSystemSettingRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var tenantId = body.TenantId;
        Guid? requiredTenantId = null;

        var command = new UpdateSystemSettingCommand(id, body.Value, body.Scope, tenantId, body.UserId, requiredTenantId);
        var result = await sender.Send(command, cancellationToken);
        return result.Match(setting => (IResult)TypedResults.Ok(setting));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        Guid? requiredTenantId = null;
        var result = await sender.Send(new DeleteSystemSettingCommand(id, requiredTenantId), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record CreateSystemSettingRequest(
    string Key,
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId);

public sealed record UpdateSystemSettingRequest(
    string Value,
    SystemSettingScope Scope,
    Guid? TenantId,
    Guid? UserId);
