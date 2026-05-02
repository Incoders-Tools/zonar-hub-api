using MediatR;
using Microsoft.AspNetCore.Mvc;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.EmailTemplates;
using ZonarHub.Application.Features.EmailTemplates.Create;
using ZonarHub.Application.Features.EmailTemplates.Delete;
using ZonarHub.Application.Features.EmailTemplates.GetAll;
using ZonarHub.Application.Features.EmailTemplates.GetById;
using ZonarHub.Application.Features.EmailTemplates.Update;

namespace ZonarHub.ApiService.Endpoints.Admin.EmailTemplates;

public static class EmailTemplatesEndpointsExtensions
{
    private const string Tag = "Admin.EmailTemplates";
    private const string RoutePrefix = "/api/admin/email-templates";

    public static IEndpointRouteBuilder MapEmailTemplatesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization("AdminOrAbove");

        group.MapGet("/", ListAsync)
            .WithName("ListEmailTemplates")
            .WithSummary("List system email templates")
            .WithDescription("Returns the editable system templates used by auth and invitation email flows.")
            .Produces<PageResult<EmailTemplateResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetEmailTemplateById")
            .WithSummary("Get one system email template")
            .Produces<EmailTemplateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAsync)
            .WithName("CreateEmailTemplate")
            .WithSummary("Create a new system email template")
            .Produces<EmailTemplateResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateEmailTemplate")
            .WithSummary("Update a system email template")
            .Produces<EmailTemplateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteEmailTemplate")
            .WithSummary("Delete a system email template")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? key = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetEmailTemplatesQuery(new EmailTemplateFilter(key, isActive, page, pageSize));
        var result = await sender.Send(query, cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetEmailTemplateByIdQuery(id), cancellationToken);
        return result.Match(t => (IResult)TypedResults.Ok(t));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateEmailTemplateRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateEmailTemplateCommand(
            body.Key,
            body.Subject,
            body.HtmlBody,
            body.Description,
            body.IsActive);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(t => TypedResults.Created($"{RoutePrefix}/{t.Id}", t));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateEmailTemplateRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateEmailTemplateCommand(
            id,
            body.Subject,
            body.HtmlBody,
            body.Description,
            body.IsActive);

        var result = await sender.Send(command, cancellationToken);
        return result.Match(t => (IResult)TypedResults.Ok(t));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteEmailTemplateCommand(id), cancellationToken);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

public sealed record CreateEmailTemplateRequest(
    string Key,
    string Subject,
    string HtmlBody,
    string? Description,
    bool IsActive);

public sealed record UpdateEmailTemplateRequest(
    string Subject,
    string HtmlBody,
    string? Description,
    bool IsActive);
