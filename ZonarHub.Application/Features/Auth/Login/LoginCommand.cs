using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.Login;

public sealed record LoginCommand(string Email, string Password)
    : IRequest<Result<AuthResponse>>;
