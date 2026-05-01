using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Courts;

public static class CourtErrors
{
    public static readonly Error NotFound = Error.NotFound("court.not_found", "courts.errors.not_found");
    public static readonly Error NameRequired = Error.Validation("court.name_required", "courts.errors.name_required");
    public static readonly Error ComplexIdRequired = Error.Validation("court.complex_id_required", "courts.errors.complex_id_required");
}
