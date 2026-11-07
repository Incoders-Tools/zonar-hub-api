using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.TournamentDraws.Preview;

/// <summary>Generates the tournament's draw from <paramref name="Body"/> without saving it (D5).</summary>
public sealed record PreviewTournamentDrawCommand(Guid TournamentId, TournamentDrawRequestBody Body)
    : IRequest<Result<TournamentDrawResponse>>;

public sealed class PreviewTournamentDrawHandler
    : IRequestHandler<PreviewTournamentDrawCommand, Result<TournamentDrawResponse>>
{
    private readonly TournamentDrawScope _scope;

    public PreviewTournamentDrawHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IOrganizationRepository organizations,
        ITournamentAdminRepository tournaments)
    {
        _scope = new TournamentDrawScope(currentUser, users, organizations, tournaments);
    }

    public async Task<Result<TournamentDrawResponse>> Handle(
        PreviewTournamentDrawCommand request,
        CancellationToken cancellationToken)
    {
        var tournament = await _scope.LoadTournamentAsync(request.TournamentId, cancellationToken);
        if (tournament.IsFailure)
            return Result.Failure<TournamentDrawResponse>(tournament.Error);

        var submission = TournamentDrawRequestMapper.ToSubmission(request.Body);
        if (submission.IsFailure)
            return Result.Failure<TournamentDrawResponse>(submission.Error);

        var draw = DrawGenerator.Generate(submission.Value.Seeded);
        if (draw.IsFailure)
            return Result.Failure<TournamentDrawResponse>(draw.Error);

        return Result.Success(TournamentDrawResponseMapper.ToResponse(
            TournamentDrawScope.Resolve(draw.Value, [], submission.Value.Scoring),
            TournamentDrawStatus.Draft,
            publishedAtUtc: null,
            submission.Value.Request.Options,
            TournamentDrawAudience.Admin));
    }
}
