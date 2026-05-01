using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.ResetPassword;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Result>;
