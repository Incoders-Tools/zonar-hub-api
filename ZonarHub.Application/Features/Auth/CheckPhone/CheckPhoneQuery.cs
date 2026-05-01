using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Auth.CheckPhone;

public sealed record CheckPhoneQuery(string Phone) : IRequest<Result<bool>>;
