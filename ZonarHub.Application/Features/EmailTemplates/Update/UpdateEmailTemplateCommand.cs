using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.EmailTemplates.Update;

public sealed record UpdateEmailTemplateCommand(
    Guid Id,
    string Subject,
    string HtmlBody,
    string? Description,
    bool IsActive) : IRequest<Result<EmailTemplateResponse>>;
