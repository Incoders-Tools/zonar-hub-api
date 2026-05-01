using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Application.Features.EmailTemplates.GetById;

public sealed class GetEmailTemplateByIdHandler
    : IRequestHandler<GetEmailTemplateByIdQuery, Result<EmailTemplateResponse>>
{
    private readonly IEmailTemplateRepository _templates;

    public GetEmailTemplateByIdHandler(IEmailTemplateRepository templates)
    {
        _templates = templates;
    }

    public async Task<Result<EmailTemplateResponse>> Handle(
        GetEmailTemplateByIdQuery request,
        CancellationToken cancellationToken)
    {
        var template = await _templates.GetByIdAsync(new EmailTemplateId(request.Id), cancellationToken);
        if (template is null)
        {
            return Result.Failure<EmailTemplateResponse>(EmailTemplateErrors.NotFound);
        }

        return Result.Success(EmailTemplateResponse.FromDomain(template));
    }
}
