namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class SupabaseOperationContext
{
    private readonly List<Func<HttpClient, CancellationToken, Task>> _pending = [];

    public void Enqueue(Func<HttpClient, CancellationToken, Task> operation)
        => _pending.Add(operation);

    public async Task<int> FlushAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var count = _pending.Count;
        foreach (var op in _pending)
            await op(http, cancellationToken);
        _pending.Clear();
        return count;
    }
}
