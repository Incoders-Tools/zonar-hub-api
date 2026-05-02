using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.EmailTemplates;

namespace ZonarHub.Application.Features.EmailTemplates.Delete;

public sealed class DeleteEmailTemplateHandler : IRequestHandler<DeleteEmailTemplateCommand, Result>
{
    private readonly IEmailTemplateRepository _emailTemplates;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteEmailTemplateHandler(
        IEmailTemplateRepository emailTemplates,
        IUnitOfWork unitOfWork)
    {
        _emailTemplates = emailTemplates;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(
        DeleteEmailTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await _emailTemplates.GetByIdAsync(
            new EmailTemplateId(request.Id),
            cancellationToken);

        if (template is null)
        {
            return Result.Failure(EmailTemplateErrors.NotFound);
        }

        _emailTemplates.Remove(template);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
