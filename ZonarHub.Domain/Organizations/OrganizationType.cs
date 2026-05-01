namespace ZonarHub.Domain.Organizations;

/// <summary>
/// Represents the operational model of an organization inside the circuit platform.
/// Values must remain lowercase to match the frontend string union type.
/// </summary>
public enum OrganizationType
{
    Estandar = 0,
    Circuito = 1,
    Academia = 2,
    Operadora = 3,
    Marca = 4,
}
