using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Users;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly InMemoryUserStore _store;
    private readonly InMemoryUserOrganizationAssignmentStore _assignments;

    public InMemoryUserRepository(InMemoryUserStore store)
        : this(store, new InMemoryUserOrganizationAssignmentStore())
    {
    }

    public InMemoryUserRepository(InMemoryUserStore store, InMemoryUserOrganizationAssignmentStore assignments)
    {
        _store = store;
        _assignments = assignments;
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

    public Task<(IReadOnlyList<User> Items, int TotalCount)> ListAsync(
        UserQuery query,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<User> source = _store.Data.Values;

        if (query.TenantId is { } tenantId)
        {
            source = source.Where(u => u.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var needle = query.Search.Trim();
            source = source.Where(u =>
                u.Email.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                u.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Role is { } role)
        {
            source = source.Where(u => u.Role == role);
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(u => u.IsActive == isActive);
        }

        if (query.Membership is { } membership)
        {
            source = source.Where(u => IsMember(u, membership));
        }

        var filtered = source
            .OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = filtered.Count;
        IReadOnlyList<User> page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult((page, total));
    }

    private bool IsMember(User user, UserOrganizationMembership membership)
    {
        var assigned = _assignments.Data.TryGetValue(user.Id.Value, out var ids)
            ? ids
            : new List<Guid>();

        if (user.OrganizationId == membership.OrganizationId || assigned.Contains(membership.OrganizationId))
        {
            return true;
        }

        return membership.IncludeUnassignedOfTenantId is { } tenantId &&
               user.TenantId == tenantId &&
               user.OrganizationId is null &&
               assigned.Count == 0;
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

    public Task RemoveAsync(UserId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
