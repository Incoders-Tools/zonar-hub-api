using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.CheckCode;

public sealed record CheckVerificationCodeQuery(string Email, string Code)
    : IRequest<Result<bool>>;
