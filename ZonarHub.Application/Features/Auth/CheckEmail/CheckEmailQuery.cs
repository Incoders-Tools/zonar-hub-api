using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.CheckEmail;

public sealed record CheckEmailQuery(string Email) : IRequest<Result<bool>>;
