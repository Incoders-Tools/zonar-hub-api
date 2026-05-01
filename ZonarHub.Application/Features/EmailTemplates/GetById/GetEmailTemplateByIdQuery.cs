using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.EmailTemplates.GetById;

public sealed record GetEmailTemplateByIdQuery(Guid Id)
    : IRequest<Result<EmailTemplateResponse>>;
