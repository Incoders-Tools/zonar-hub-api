using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.SystemSettings;
using ZonarHub.Domain.Tournaments;
using MediatR;

namespace ZonarHub.Application.Features.Onboarding.CompleteOnboarding;

public sealed class CompleteOnboardingHandler
    : IRequestHandler<CompleteOnboardingCommand, Result<CompleteOnboardingResponse>>
{
    private readonly IOrganizationRepository _organizations;
    private readonly ISystemSettingRepository _settings;
    private readonly IComplexRepository _complexes;
    private readonly ICourtRepository _courts;
    private readonly ISportRepository _sports;
    private readonly IOrganizationSportRepository _orgSports;
    private readonly ITournamentRepository _tournaments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CompleteOnboardingHandler(
        IOrganizationRepository organizations,
        ISystemSettingRepository settings,
        IComplexRepository complexes,
        ICourtRepository courts,
        ISportRepository sports,
        IOrganizationSportRepository orgSports,
        ITournamentRepository tournaments,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _organizations = organizations;
        _settings = settings;
        _complexes = complexes;
        _courts = courts;
        _sports = sports;
        _orgSports = orgSports;
        _tournaments = tournaments;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<CompleteOnboardingResponse>> Handle(
        CompleteOnboardingCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // Step 1 — Create organization
        var orgResult = Organization.Create(
            OrganizationId.New(),
            request.TenantId,
            request.OrganizationDisplayName,
            legalName: null,
            description: null,
            request.OrganizationType,
            logoUrl: null,
            request.CreatedByUserId,
            now);

        if (orgResult.IsFailure)
            return Result.Failure<CompleteOnboardingResponse>(orgResult.Error);

        var org = orgResult.Value;
        await _organizations.AddAsync(org, cancellationToken);

        // Step 2 — Persist system settings (upsert each non-null value)
        if (request.SystemSettings is { } sysSettings)
        {
            var upserts = new List<(string Key, string? Value)>
            {
                ("app.locale",     sysSettings.Locale),
                ("app.theme",      sysSettings.Theme),
                ("app.timezone",   sysSettings.Timezone),
                ("app.dateFormat", sysSettings.DateFormat),
            };

            foreach (var (key, value) in upserts)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;

                var existing = await _settings.GetByKeyAsync(
                    key, SystemSettingScope.Tenant, request.TenantId, userId: null, cancellationToken);

                if (existing is not null)
                {
                    var updateResult = existing.Update(value, SystemSettingScope.Tenant, request.TenantId, userId: null, now);
                    if (updateResult.IsFailure)
                        return Result.Failure<CompleteOnboardingResponse>(updateResult.Error);

                    _settings.Update(existing);
                }
                else
                {
                    var createResult = SystemSetting.Create(
                        SystemSettingId.New(), key, value,
                        SystemSettingScope.Tenant, request.TenantId, userId: null, now);

                    if (createResult.IsFailure)
                        return Result.Failure<CompleteOnboardingResponse>(createResult.Error);

                    await _settings.AddAsync(createResult.Value, cancellationToken);
                }
            }
        }

        // Step 3 — Create complex + courts
        ComplexId? complexId = null;
        var courtIds = new List<Guid>();

        if (request.Venue is { } venue)
        {
            var complexResult = Complex.Create(
                ComplexId.New(), org.Id, venue.Name, venue.Address, venue.Location, now);

            if (complexResult.IsFailure)
                return Result.Failure<CompleteOnboardingResponse>(complexResult.Error);

            var complex = complexResult.Value;
            await _complexes.AddAsync(complex, cancellationToken);
            complexId = complex.Id;

            foreach (var courtName in venue.CourtNames)
            {
                var courtResult = Court.Create(CourtId.New(), complex.Id, courtName, now);
                if (courtResult.IsFailure)
                    return Result.Failure<CompleteOnboardingResponse>(courtResult.Error);

                await _courts.AddAsync(courtResult.Value, cancellationToken);
                courtIds.Add(courtResult.Value.Id.Value);
            }
        }

        // Step 4 — Enable sports for organization
        var sportIds = request.EnabledSportIds.Distinct().Select(id => new SportId(id)).ToList();

        var foundSports = await _sports.GetByIdsAsync(sportIds, cancellationToken);
        if (foundSports.Count != sportIds.Count)
            return Result.Failure<CompleteOnboardingResponse>(SportErrors.NotFound);

        await _orgSports.SetEnabledSportsAsync(org.Id, sportIds, cancellationToken);

        // Step 5 — Create tournament (optional)
        Guid? tournamentId = null;

        if (request.Tournament is { } t)
        {
            var tournamentResult = Tournament.Create(
                TournamentId.New(),
                org.Id,
                complexId,
                sportIds[0],
                t.Name,
                t.StartDate,
                t.EndDate,
                now);

            if (tournamentResult.IsFailure)
                return Result.Failure<CompleteOnboardingResponse>(tournamentResult.Error);

            await _tournaments.AddAsync(tournamentResult.Value, cancellationToken);
            tournamentId = tournamentResult.Value.Id.Value;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CompleteOnboardingResponse(
            org.Id.Value,
            complexId?.Value,
            courtIds,
            sportIds.Select(s => s.Value).ToList(),
            tournamentId));
    }
}
