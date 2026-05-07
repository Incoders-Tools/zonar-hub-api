using Microsoft.AspNetCore.Mvc;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.ApiService.Endpoints.Admin.Catalog.Genders;

public static class CatalogGendersEndpointsExtensions
{
    private const string Tag = "Catalog.Genders";
    private const string RoutePrefix = "/api/catalog/genders";

    public static IEndpointRouteBuilder MapCatalogGendersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization();

        group.MapGet("/", GetAllAsync)
            .AllowAnonymous()
            .WithName("GetAllGenders")
            .WithSummary("Get all genders")
            .WithDescription("Returns all genders available in the catalog.")
            .Produces<IReadOnlyList<GenderResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .AllowAnonymous()
            .WithName("GetGenderById")
            .WithSummary("Get gender by ID")
            .Produces<GenderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateGender")
            .WithSummary("Create a new gender")
            .Produces<GenderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateGender")
            .WithSummary("Update a gender")
            .Produces<GenderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteGender")
            .WithSummary("Delete a gender")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        IGenderRepository repo,
        CancellationToken ct)
    {
        var items = await repo.GetAllAsync(ct);
        return Results.Ok(items.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        IGenderRepository repo,
        CancellationToken ct)
    {
        var item = await repo.GetByIdAsync(id, ct);
        return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] GenderWriteRequest body,
        IGenderRepository repo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(body.Key))
            return Results.BadRequest("Key is required.");

        var now = DateTime.UtcNow;
        var dto = new GenderDto(Guid.NewGuid(), body.Name, body.Key, body.IsActive, body.SortOrder, now, now);
        var created = await repo.AddAsync(dto, ct);
        return Results.Created($"{RoutePrefix}/{created.Id}", ToResponse(created));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] GenderWriteRequest body,
        IGenderRepository repo,
        CancellationToken ct)
    {
        var existing = await repo.GetByIdAsync(id, ct);
        if (existing is null)
            return Results.NotFound();

        var dto = new GenderDto(id, body.Name, body.Key, body.IsActive, body.SortOrder, existing.CreatedAtUtc, DateTime.UtcNow);
        var updated = await repo.UpdateAsync(id, dto, ct);
        return updated is null ? Results.NotFound() : Results.Ok(ToResponse(updated));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        IGenderRepository repo,
        CancellationToken ct)
    {
        var deleted = await repo.DeleteAsync(id, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static GenderResponse ToResponse(GenderDto dto) => new(
        dto.Id, dto.Name, dto.Key, dto.IsActive, dto.SortOrder,
        dto.CreatedAtUtc, dto.UpdatedAtUtc);
}

internal sealed record GenderResponse(
    Guid Id,
    string Name,
    string Key,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt);

internal sealed record GenderWriteRequest(
    string Name,
    string Key,
    bool IsActive,
    int SortOrder);
