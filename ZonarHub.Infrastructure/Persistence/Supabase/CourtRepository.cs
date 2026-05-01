using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class CourtRepository : ICourtRepository
{
    private readonly SupabaseOptions _options;

    public CourtRepository(IOptions<SupabaseOptions> options)
    {
        _options = options.Value;
    }

    public Task<IReadOnlyList<Court>> ListByComplexIdAsync(
        ComplexId complexId,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task AddAsync(Court court, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
