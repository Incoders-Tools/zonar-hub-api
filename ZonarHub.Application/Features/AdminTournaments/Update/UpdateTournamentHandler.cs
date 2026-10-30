using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminTournaments.Update;

public sealed class UpdateTournamentHandler
    : IRequestHandler<UpdateTournamentCommand, Result<TournamentResponse>>
{
    private readonly TournamentOrganizationAccess _access;
    private readonly ITournamentAdminRepository _tournaments;
    private readonly IClock _clock;

    public UpdateTournamentHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        IClock clock)
    {
        _access = new TournamentOrganizationAccess(currentUser, users, organizations);
        _tournaments = tournaments;
        _clock = clock;
    }

    public async Task<Result<TournamentResponse>> Handle(
        UpdateTournamentCommand request,
        CancellationToken cancellationToken)
    {
        var current = await _tournaments.GetByIdAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.NotFound);
        }

        if (!await _access.CanAccessAsync(current.OrganizationId, cancellationToken))
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.Forbidden);
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.NameRequired);
        }

        if (request.SportId == Guid.Empty)
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.SportRequired);
        }

        if (request.EndDate < request.StartDate)
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.InvalidDateRange);
        }

        var dto = current with
        {
            Name = name,
            Key = string.IsNullOrWhiteSpace(request.Key) ? null : request.Key.Trim(),
            ComplexId = request.ComplexId,
            SportId = request.SportId,
            CategoryId = request.CategoryId,
            GenderId = request.GenderId,
            ModalityId = request.ModalityId,
            TournamentTypeId = request.TournamentTypeId,
            RuleSetId = request.RuleSetId,
            Status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            RegistrationStartDate = request.RegistrationStartDate,
            RegistrationEndDate = request.RegistrationEndDate,
            MaxPairs = request.MaxPairs,
            Description = request.Description,
            Rules = request.Rules,
            ImageUrl = request.ImageUrl,
            CoverImageUrl = request.CoverImageUrl,
            RegistrationFeePerPair = request.RegistrationFeePerPair,
            PrizeMoney = request.PrizeMoney,
            PointsToAward = request.PointsToAward,
            SumValue = request.SumValue,
            Observations = request.Observations,
            IsActive = request.IsActive,
            SelectedCourtIds = request.SelectedCourtIds ?? [],
            UpdatedAtUtc = _clock.UtcNow
        };

        var updated = await _tournaments.UpdateAsync(dto, cancellationToken);
        return Result.Success(TournamentMapper.ToResponse(updated));
    }
}
