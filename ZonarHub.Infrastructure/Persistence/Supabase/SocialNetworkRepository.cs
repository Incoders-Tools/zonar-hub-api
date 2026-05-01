using Microsoft.Extensions.Options;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.SocialNetworks;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class SocialNetworkRepository : ISocialNetworkRepository
{
    private readonly SupabaseOptions _options;

    public SocialNetworkRepository(IOptions<SupabaseOptions> options)
    {
        _options = options.Value;
    }

    public Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<SocialNetwork?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<(IReadOnlyList<SocialNetwork> Items, int TotalCount)> ListAsync(
        SocialNetworkQuery query,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task AddAsync(SocialNetwork network, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public void Update(SocialNetwork network)
        => throw new NotImplementedException();

    public void Remove(SocialNetwork network)
        => throw new NotImplementedException();
}
