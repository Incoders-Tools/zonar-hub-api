using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminDashboard.GetSummary;

public sealed record GetAdminDashboardSummaryQuery(Guid OrganizationId)
    : IRequest<Result<AdminDashboardSummaryResponse>>;
