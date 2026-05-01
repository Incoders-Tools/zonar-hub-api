using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.SendVerificationCode;

public sealed record SendVerificationCodeCommand(string Email) : IRequest<Result>;
