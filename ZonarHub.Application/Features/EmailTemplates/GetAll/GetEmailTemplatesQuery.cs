using MediatR;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.EmailTemplates.GetAll;

public sealed record GetEmailTemplatesQuery(EmailTemplateFilter Filter)
    : IRequest<Result<PageResult<EmailTemplateResponse>>>;

public sealed record EmailTemplateFilter(
    string? Key = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20);
