using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.EmailTemplates.GetAll;

public sealed class GetEmailTemplatesHandler
    : IRequestHandler<GetEmailTemplatesQuery, Result<PageResult<EmailTemplateResponse>>>
{
    private readonly IEmailTemplateRepository _templates;

    public GetEmailTemplatesHandler(IEmailTemplateRepository templates)
    {
        _templates = templates;
    }

    public async Task<Result<PageResult<EmailTemplateResponse>>> Handle(
        GetEmailTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request.Filter);
        var query = new EmailTemplateQuery(
            normalized.Key,
            normalized.IsActive,
            normalized.Page,
            normalized.PageSize);

        var (items, total) = await _templates.ListAsync(query, cancellationToken);
        IReadOnlyList<EmailTemplateResponse> mapped = items
            .Select(EmailTemplateResponse.FromDomain)
            .ToList();

        return Result.Success(new PageResult<EmailTemplateResponse>(
            mapped,
            normalized.Page,
            normalized.PageSize,
            total));
    }

    private static EmailTemplateFilter Normalize(EmailTemplateFilter filter)
    {
        var page = filter.Page < 1 ? PageRequest.DefaultPage : filter.Page;
        var pageSize = filter.PageSize switch
        {
            < 1 => PageRequest.DefaultPageSize,
            > PageRequest.MaxPageSize => PageRequest.MaxPageSize,
            _ => filter.PageSize,
        };

        var key = string.IsNullOrWhiteSpace(filter.Key) ? null : filter.Key.Trim();
        return filter with { Key = key, Page = page, PageSize = pageSize };
    }
}
