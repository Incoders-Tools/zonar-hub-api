using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.Register;

public sealed record RegisterCommand(
    string FullName,
    string Email,
    string Password,
    string? Phone,
    string? BirthDate,
    string VerificationCode) : IRequest<Result<AuthResponse>>;
