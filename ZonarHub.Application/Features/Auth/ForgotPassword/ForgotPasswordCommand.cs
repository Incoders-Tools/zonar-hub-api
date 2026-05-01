using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email, string ResetUrlBase)
    : IRequest<Result>;
