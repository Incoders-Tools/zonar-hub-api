using FluentValidation;

namespace ZonarHub.Application.Features.AdminDashboard.GetSummary;

internal sealed class GetAdminDashboardSummaryValidator : AbstractValidator<GetAdminDashboardSummaryQuery>
{
    public GetAdminDashboardSummaryValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty()
            .WithMessage("admin_dashboard.errors.organization_required");
    }
}
