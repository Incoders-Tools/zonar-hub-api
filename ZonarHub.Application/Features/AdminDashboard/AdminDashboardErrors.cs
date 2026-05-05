using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminDashboard;

internal static class AdminDashboardErrors
{
    public static readonly Error OrganizationRequired = Error.Validation(
        "admin_dashboard.organization_required",
        "admin_dashboard.errors.organization_required");

    public static readonly Error OrganizationNotFound = Error.NotFound(
        "admin_dashboard.organization_not_found",
        "admin_dashboard.errors.organization_not_found");
}
