using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class ComplexRepository : IComplexRepository
{
    private readonly SupabaseOptions _options;

    public ComplexRepository(IOptions<SupabaseOptions> options)
    {
        _options = options.Value;
    }

    public Task<Complex?> GetByIdAsync(ComplexId id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task AddAsync(Complex complex, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
