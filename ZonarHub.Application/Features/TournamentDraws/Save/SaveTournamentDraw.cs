using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentDraws.Save;

/// <summary>Generates the tournament's draw from <paramref name="Body"/> and saves it as the draft (D1, D5).</summary>
public sealed record SaveTournamentDrawCommand(Guid TournamentId, TournamentDrawRequestBody Body)
    : IRequest<Result<TournamentDrawResponse>>;

/// <remarks>
/// Fails with <see cref="TournamentDrawErrors.ResultsExist"/> while any match of the stored draw has a result other
/// than scheduled. The save is written at the revision that check read (D12): when the draw changed meanwhile (a result
/// recorded, another save, a delete) or was created by someone else, nothing is saved and the request fails with
/// <see cref="TournamentDrawErrors.ConcurrentModification"/>.
/// </remarks>
public sealed class SaveTournamentDrawHandler
    : IRequestHandler<SaveTournamentDrawCommand, Result<TournamentDrawResponse>>
{
    private readonly TournamentDrawScope _scope;
    private readonly ITournamentDrawRepository _draws;

    public SaveTournamentDrawHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments,
        ITournamentDrawRepository draws)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
        _draws = draws;
    }

    public async Task<Result<TournamentDrawResponse>> Handle(
        SaveTournamentDrawCommand request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentDrawResponse>(tournament.Error);

        var existing = await _draws.GetByTournamentIdAsync(request.TournamentId, cancellationToken);
        if (existing is not null
            && TournamentDrawScope.HasResults(TournamentDrawScope.CurrentResults(existing.Draw.Structure, existing.Results)))
            return Result.Failure<TournamentDrawResponse>(TournamentDrawErrors.ResultsExist);

        var submission = TournamentDrawRequestMapper.ToSubmission(request.Body);
        if (submission.IsFailure)
            return Result.Failure<TournamentDrawResponse>(submission.Error);

        var draw = DrawGenerator.Generate(submission.Value.Seeded);
        if (draw.IsFailure)
            return Result.Failure<TournamentDrawResponse>(draw.Error);

        var saved = await TournamentDrawScope.WriteAsync(() => _draws.SaveDraftAsync(
            new TournamentDrawDraft(
                tournament.Value.Id,
                tournament.Value.OrganizationId,
                submission.Value.Request,
                submission.Value.Scoring,
                draw.Value),
            existing?.Draw.Version,
            cancellationToken));
        if (saved.IsFailure)
            return Result.Failure<TournamentDrawResponse>(saved.Error);

        // The revision check guarantees the stored results are still the ones read: scheduled rows (court or time only)
        // stay attached, and those of matches the new draw lacks are ignored.
        return Result.Success(TournamentDrawScope.ToAdminResponse(
            saved.Value, TournamentDrawScope.CurrentResults(saved.Value.Structure, existing?.Results ?? [])));
    }
}
