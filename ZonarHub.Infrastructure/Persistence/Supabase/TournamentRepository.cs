using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class TournamentRepository : ITournamentRepository
{
    private readonly SupabaseOptions _options;

    public TournamentRepository(IOptions<SupabaseOptions> options)
    {
        _options = options.Value;
    }

    public Task<Tournament?> GetByIdAsync(TournamentId id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
