using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.EligibilityProfiles;

/// <summary>Creates an active eligibility profile of the organization.</summary>
public sealed record CreateEligibilityProfileCommand(Guid OrganizationId, EligibilityProfileBody Body)
    : IRequest<Result<EligibilityProfileResponse>>;

/// <summary>Replaces the name, description, activity and every slot of the organization's profile.</summary>
public sealed record UpdateEligibilityProfileCommand(Guid OrganizationId, Guid ProfileId, EligibilityProfileBody Body)
    : IRequest<Result<EligibilityProfileResponse>>;

/// <summary>Deletes the organization's profile; refused while a tournament references it.</summary>
public sealed record DeleteEligibilityProfileCommand(Guid OrganizationId, Guid ProfileId) : IRequest<Result>;

/// <remarks>
/// Order: tenant rule (<see cref="AdminTournamentErrors.Forbidden"/>), the <see cref="EligibilityProfile"/> rules, catalog
/// references, then the store's unique name (<see cref="EligibilityProfileErrors.NameTaken"/>). Nothing is written on a
/// failure.
/// </remarks>
public sealed class CreateEligibilityProfileHandler
    : IRequestHandler<CreateEligibilityProfileCommand, Result<EligibilityProfileResponse>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IEligibilityProfileRepository _profiles;
    private readonly CatalogReferences _catalogs;
    private readonly IClock _clock;

    public CreateEligibilityProfileHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        IEligibilityProfileRepository profiles,
        IGenderRepository genders,
        ICategoryRepository categories,
        IClock clock)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _profiles = profiles;
        _catalogs = new CatalogReferences(genders, categories);
        _clock = clock;
    }

    public async Task<Result<EligibilityProfileResponse>> Handle(CreateEligibilityProfileCommand request, CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure<EligibilityProfileResponse>(AdminTournamentErrors.Forbidden);

        var created = EligibilityProfile.Create(
            EligibilityProfileId.New(),
            new OrganizationId(request.OrganizationId),
            request.Body.Name!,
            request.Body.Description,
            EligibilityProfileMapper.ToSlots(request.Body),
            _clock.UtcNow);
        if (created.IsFailure)
            return Result.Failure<EligibilityProfileResponse>(created.Error);

        return await SaveAsync(_profiles, _catalogs, created.Value, cancellationToken);
    }

    /// <summary>Checks the slots' catalog references, then saves the profile and returns it as stored.</summary>
    internal static async Task<Result<EligibilityProfileResponse>> SaveAsync(
        IEligibilityProfileRepository profiles,
        CatalogReferences catalogs,
        EligibilityProfile profile,
        CancellationToken cancellationToken)
    {
        var references = await EligibilityProfileMapper.CheckReferencesAsync(catalogs, profile.Slots, cancellationToken);
        if (references.IsFailure)
            return Result.Failure<EligibilityProfileResponse>(references.Error);

        var saved = await PersistenceWrites.TryAsync(() => profiles.SaveAsync(profile, cancellationToken));
        return saved.IsFailure
            ? Result.Failure<EligibilityProfileResponse>(saved.Error)
            : Result.Success(EligibilityProfileMapper.ToResponse(saved.Value));
    }
}

/// <remarks>
/// Same order and errors as <see cref="CreateEligibilityProfileHandler"/>, after <see cref="EligibilityProfileErrors.NotFound"/>
/// for an unknown id or another organization's profile.
/// </remarks>
public sealed class UpdateEligibilityProfileHandler
    : IRequestHandler<UpdateEligibilityProfileCommand, Result<EligibilityProfileResponse>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IEligibilityProfileRepository _profiles;
    private readonly CatalogReferences _catalogs;
    private readonly IClock _clock;

    public UpdateEligibilityProfileHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        IEligibilityProfileRepository profiles,
        IGenderRepository genders,
        ICategoryRepository categories,
        IClock clock)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _profiles = profiles;
        _catalogs = new CatalogReferences(genders, categories);
        _clock = clock;
    }

    public async Task<Result<EligibilityProfileResponse>> Handle(UpdateEligibilityProfileCommand request, CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure<EligibilityProfileResponse>(AdminTournamentErrors.Forbidden);

        var profile = await _profiles.GetByIdAsync(
            new OrganizationId(request.OrganizationId), new EligibilityProfileId(request.ProfileId), cancellationToken);
        if (profile is null)
            return Result.Failure<EligibilityProfileResponse>(EligibilityProfileErrors.NotFound);

        var updated = profile.Update(
            request.Body.Name!, request.Body.Description, request.Body.IsActive, EligibilityProfileMapper.ToSlots(request.Body), _clock.UtcNow);
        if (updated.IsFailure)
            return Result.Failure<EligibilityProfileResponse>(updated.Error);

        return await CreateEligibilityProfileHandler.SaveAsync(_profiles, _catalogs, profile, cancellationToken);
    }
}

/// <remarks>
/// Tenant rule first; <see cref="EligibilityProfileErrors.NotFound"/> when the organization has no such profile and
/// <see cref="EligibilityProfileErrors.InUse"/> while a tournament references it (nothing is deleted).
/// </remarks>
public sealed class DeleteEligibilityProfileHandler : IRequestHandler<DeleteEligibilityProfileCommand, Result>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly IEligibilityProfileRepository _profiles;

    public DeleteEligibilityProfileHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        IEligibilityProfileRepository profiles)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _profiles = profiles;
    }

    public async Task<Result> Handle(DeleteEligibilityProfileCommand request, CancellationToken cancellationToken)
    {
        if (!await _access.CanAccessAsync(request.OrganizationId, cancellationToken))
            return Result.Failure(AdminTournamentErrors.Forbidden);

        var deleted = await PersistenceWrites.TryAsync(() => _profiles.DeleteAsync(
            new OrganizationId(request.OrganizationId), new EligibilityProfileId(request.ProfileId), cancellationToken));
        if (deleted.IsFailure)
            return Result.Failure(deleted.Error);

        return deleted.Value ? Result.Success() : Result.Failure(EligibilityProfileErrors.NotFound);
    }
}
