using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Application.Features.EmailTemplates.Update;

public sealed class UpdateEmailTemplateHandler
    : IRequestHandler<UpdateEmailTemplateCommand, Result<EmailTemplateResponse>>
{
    private readonly IEmailTemplateRepository _templates;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateEmailTemplateHandler(
        IEmailTemplateRepository templates,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _templates = templates;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<EmailTemplateResponse>> Handle(
        UpdateEmailTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await _templates.GetByIdAsync(new EmailTemplateId(request.Id), cancellationToken);
        if (template is null)
        {
            return Result.Failure<EmailTemplateResponse>(EmailTemplateErrors.NotFound);
        }

        var updated = template.Update(
            request.Subject,
            request.HtmlBody,
            request.Description,
            request.IsActive,
            _clock.UtcNow);

        if (updated.IsFailure)
        {
            return Result.Failure<EmailTemplateResponse>(updated.Error);
        }

        _templates.Update(template);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(EmailTemplateResponse.FromDomain(template));
    }
}
