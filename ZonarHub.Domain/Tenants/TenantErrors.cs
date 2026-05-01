using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Tenants;

public static class TenantErrors
{
    public static readonly Error NotFound = Error.NotFound("tenant.not_found", "tenants.errors.not_found");
    public static readonly Error NameRequired = Error.Validation("tenant.name_required", "tenants.errors.name_required");
    public static readonly Error ContactEmailRequired = Error.Validation("tenant.contact_email_required", "tenants.errors.contact_email_required");
}
