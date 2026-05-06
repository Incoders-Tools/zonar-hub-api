using Microsoft.AspNetCore.Mvc;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.ApiService.Endpoints.Catalog;

public static class CatalogCategoriesEndpointsExtensions
{
    private const string Tag = "Catalog.Categories";
    private const string RoutePrefix = "/api/catalog/categories";

    public static IEndpointRouteBuilder MapCatalogCategoriesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(RoutePrefix)
            .WithTags(Tag)
            .RequireAuthorization();

        group.MapGet("/", GetAllAsync)
            .AllowAnonymous()
            .WithName("GetAllCategories")
            .WithSummary("Get all categories")
            .WithDescription("Returns all categories available in the catalog.")
            .Produces<IReadOnlyList<CategoryResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .AllowAnonymous()
            .WithName("GetCategoryById")
            .WithSummary("Get category by ID")
            .Produces<CategoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateCategory")
            .WithSummary("Create a new category")
            .Produces<CategoryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateCategory")
            .WithSummary("Update a category")
            .Produces<CategoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteCategory")
            .WithSummary("Delete a category")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetAllAsync(
        ICategoryRepository repo,
        CancellationToken ct)
    {
        var items = await repo.GetAllAsync(ct);
        return Results.Ok(items.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ICategoryRepository repo,
        CancellationToken ct)
    {
        var item = await repo.GetByIdAsync(id, ct);
        return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CategoryWriteRequest body,
        ICategoryRepository repo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(body.ShortName))
            return Results.BadRequest("ShortName is required.");
        if (string.IsNullOrWhiteSpace(body.Key))
            return Results.BadRequest("Key is required.");

        var now = DateTime.UtcNow;
        var dto = new CategoryDto(Guid.NewGuid(), body.Name, body.ShortName, body.Key, body.Level, body.IsActive, body.SortOrder, now, now);
        var created = await repo.AddAsync(dto, ct);
        return Results.Created($"{RoutePrefix}/{created.Id}", ToResponse(created));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] CategoryWriteRequest body,
        ICategoryRepository repo,
        CancellationToken ct)
    {
        var existing = await repo.GetByIdAsync(id, ct);
        if (existing is null)
            return Results.NotFound();

        var dto = new CategoryDto(id, body.Name, body.ShortName, body.Key, body.Level, body.IsActive, body.SortOrder, existing.CreatedAtUtc, DateTime.UtcNow);
        var updated = await repo.UpdateAsync(id, dto, ct);
        return updated is null ? Results.NotFound() : Results.Ok(ToResponse(updated));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ICategoryRepository repo,
        CancellationToken ct)
    {
        var deleted = await repo.DeleteAsync(id, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static CategoryResponse ToResponse(CategoryDto dto) => new(
        dto.Id, dto.Name, dto.ShortName, dto.Key, dto.Level, dto.IsActive, dto.SortOrder,
        dto.CreatedAtUtc, dto.UpdatedAtUtc);
}

internal sealed record CategoryResponse(
    Guid Id,
    string Name,
    string ShortName,
    string Key,
    int Level,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt);

internal sealed record CategoryWriteRequest(
    string Name,
    string ShortName,
    string Key,
    int Level,
    bool IsActive,
    int SortOrder);
