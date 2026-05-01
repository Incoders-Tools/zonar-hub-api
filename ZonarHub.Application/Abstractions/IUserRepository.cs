using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    Task<User?> GetByRefreshTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<User> Items, int TotalCount)> ListAsync(
        UserQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    void Update(User user);

    Task RemoveAsync(UserId id, CancellationToken cancellationToken = default);
}

public sealed record UserQuery(
    Guid? TenantId,
    string? Search,
    UserRole? Role,
    bool? IsActive,
    int Page,
    int PageSize);
