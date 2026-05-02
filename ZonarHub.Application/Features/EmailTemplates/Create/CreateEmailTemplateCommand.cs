using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.EmailTemplates.Create;

public sealed record CreateEmailTemplateCommand(
    string Key,
    string Subject,
    string HtmlBody,
    string? Description,
    bool IsActive) : IRequest<Result<EmailTemplateResponse>>;
