using System.Net;
using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.Complexes.SaveWithCourts;

public sealed class SaveComplexWithCourtsHandler(
    ICurrentUser currentUser, IUserRepository users,
    IUserOrganizationAssignmentRepository assignments, IOrganizationRepository organizations,
    IComplexRepository complexes)
    : IRequestHandler<SaveComplexWithCourtsCommand, Result<SavedComplexWithCourtsData>>
{
    private static Error Invalid => Error.Validation("complexes.aggregate_invalid", "complexes.errors.aggregate_invalid");
    private static Error Forbidden => Error.Failure("complexes.aggregate_forbidden", "complexes.errors.aggregate_forbidden");

    public async Task<Result<SavedComplexWithCourtsData>> Handle(SaveComplexWithCourtsCommand request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty || request.ComplexId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Address) ||
            request.Courts is null || request.DeleteCourtIds is null ||
            request.DeleteCourtIds.Any(id => id == Guid.Empty) ||
            request.DeleteCourtIds.Count != request.DeleteCourtIds.Distinct().Count() ||
            request.Courts.Any(c => c is null || c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Name) ||
                c.SportIds is { } sports && (sports.Any(id => id == Guid.Empty) || sports.Count != sports.Distinct().Count())) ||
            request.Courts.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).Distinct().Count() != request.Courts.Count(c => c.Id.HasValue) ||
            request.Courts.Any(c => c.Id.HasValue && request.DeleteCourtIds.Contains(c.Id.Value)))
            return Result.Failure<SavedComplexWithCourtsData>(Invalid);

        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            return Result.Failure<SavedComplexWithCourtsData>(Forbidden);
        var caller = await users.GetByIdAsync(new UserId(currentUser.UserId.Value), ct);
        if (caller is null || !caller.IsActive || caller.Role is not (UserRole.Admin or UserRole.SystemAdmin))
            return Result.Failure<SavedComplexWithCourtsData>(Forbidden);
        var organization = await organizations.GetByIdAsync(new OrganizationId(request.OrganizationId), ct);
        if (organization is null || !organization.IsActive)
            return Result.Failure<SavedComplexWithCourtsData>(Invalid);
        if (caller.Role != UserRole.SystemAdmin)
        {
            var assigned = await assignments.GetOrganizationIdsByUserIdAsync(caller.Id.Value, ct);
            if (caller.TenantId != organization.TenantId || !assigned.Contains(request.OrganizationId))
                return Result.Failure<SavedComplexWithCourtsData>(Forbidden);
        }
        if (request.ComplexId is { } existingId)
        {
            var existing = await complexes.GetByIdAsync(new ComplexId(existingId), ct);
            if (existing is null || existing.OrganizationId.Value != request.OrganizationId)
                return Result.Failure<SavedComplexWithCourtsData>(Invalid);
        }
        var data = new SaveComplexWithCourtsData(request.ComplexId, request.OrganizationId,
            request.Name.Trim(), request.Address.Trim(), request.Key, request.Location,
            request.Description, request.SortOrder, request.Preponderance, request.LogoImagePath,
            request.CoverImagePath, request.LayoutDiagramPath, request.IsActive, request.Courts, request.DeleteCourtIds);
        try
        {
            return Result.Success(await complexes.SaveWithCourtsAsync(data, ct));
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            return Result.Failure<SavedComplexWithCourtsData>(Invalid);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return Result.Failure<SavedComplexWithCourtsData>(
                Error.Conflict("complexes.aggregate_conflict", "complexes.errors.aggregate_conflict"));
        }
    }
}
