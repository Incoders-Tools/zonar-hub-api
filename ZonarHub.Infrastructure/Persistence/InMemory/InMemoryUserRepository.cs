using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Users;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly InMemoryUserStore _store;

    public InMemoryUserRepository(InMemoryUserStore store)
    {
        _store = store;
    }

    public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryGetValue(id, out var user);
        return Task.FromResult(user);
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = _store.Data.Values.FirstOrDefault(u =>
            string.Equals(u.Email, email.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user);
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var exists = _store.Data.Values.Any(u =>
            string.Equals(u.Email, email.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    public Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var normalized = phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        var exists = _store.Data.Values.Any(u =>
            u.Phone?.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "") == normalized);
        return Task.FromResult(exists);
    }

    public Task<User?> GetByRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var user = _store.Data.Values.FirstOrDefault(u =>
            u.RefreshToken is not null &&
            string.Equals(u.RefreshToken, token, StringComparison.Ordinal));
        return Task.FromResult(user);
    }

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(user.Id, user))
            throw new InvalidOperationException($"User '{user.Id}' already exists.");
        return Task.CompletedTask;
    }

    public void Update(User user)
    {
        _store.Data[user.Id] = user;
    }
}
