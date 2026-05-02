using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Application.Features.EmailTemplates.Create;

public sealed class CreateEmailTemplateHandler
    : IRequestHandler<CreateEmailTemplateCommand, Result<EmailTemplateResponse>>
{
    private readonly IEmailTemplateRepository _emailTemplates;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateEmailTemplateHandler(
        IEmailTemplateRepository emailTemplates,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _emailTemplates = emailTemplates;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<EmailTemplateResponse>> Handle(
        CreateEmailTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _emailTemplates.GetByKeyAsync(request.Key, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<EmailTemplateResponse>(EmailTemplateErrors.DuplicateKey);
        }

        var created = EmailTemplate.Create(
            EmailTemplateId.New(),
            request.Key,
            request.Subject,
            request.HtmlBody,
            request.Description,
            request.IsActive,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<EmailTemplateResponse>(created.Error);
        }

        await _emailTemplates.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(EmailTemplateResponse.FromDomain(created.Value));
    }
}
