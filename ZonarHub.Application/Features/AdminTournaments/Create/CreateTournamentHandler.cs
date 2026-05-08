using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminTournaments.Create;

public sealed class CreateTournamentHandler
    : IRequestHandler<CreateTournamentCommand, Result<TournamentResponse>>
{
    private readonly ITournamentAdminRepository _tournaments;
    private readonly IClock _clock;

    public CreateTournamentHandler(ITournamentAdminRepository tournaments, IClock clock)
    {
        _tournaments = tournaments;
        _clock = clock;
    }

    public async Task<Result<TournamentResponse>> Handle(
        CreateTournamentCommand request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<TournamentResponse>(AdminTournamentErrors.OrganizationRequired);
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

        var nowUtc = _clock.UtcNow;
        var dto = new TournamentAdminDto(
            Guid.NewGuid(),
            request.OrganizationId,
            name,
            string.IsNullOrWhiteSpace(request.Key) ? null : request.Key.Trim(),
            request.ComplexId,
            request.SportId,
            request.CategoryId,
            request.GenderId,
            request.ModalityId,
            request.TournamentTypeId,
            request.RuleSetId,
            string.IsNullOrWhiteSpace(request.Status) ? null : request.Status,
            request.StartDate,
            request.EndDate,
            request.RegistrationStartDate,
            request.RegistrationEndDate,
            request.MaxPairs,
            request.Description,
            request.Rules,
            request.ImageUrl,
            request.CoverImageUrl,
            request.RegistrationFeePerPair,
            request.PrizeMoney,
            request.PointsToAward,
            request.SumValue,
            request.Observations,
            request.IsActive,
            request.SelectedCourtIds ?? [],
            nowUtc,
            nowUtc);

        var inserted = await _tournaments.AddAsync(dto, cancellationToken);
        return Result.Success(TournamentMapper.ToResponse(inserted));
    }
}
