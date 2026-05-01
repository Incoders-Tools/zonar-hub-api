using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public UnitOfWork(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _ops.FlushAsync(_http, cancellationToken);
}
