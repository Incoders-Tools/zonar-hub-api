using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Complexes;

public static class ComplexErrors
{
    public static readonly Error NotFound = Error.NotFound("complex.not_found", "complexes.errors.not_found");
    public static readonly Error NameRequired = Error.Validation("complex.name_required", "complexes.errors.name_required");
    public static readonly Error AddressRequired = Error.Validation("complex.address_required", "complexes.errors.address_required");
    public static readonly Error OrganizationIdRequired = Error.Validation("complex.organization_id_required", "complexes.errors.organization_id_required");
}
