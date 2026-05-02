using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.EmailTemplates.Delete;

public sealed record DeleteEmailTemplateCommand(Guid Id) : IRequest<Result>;
