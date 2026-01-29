namespace MimosBabySpa.Application.FlowEngine;

/// <summary>
/// Configuración de campos requeridos para un negocio específico.
/// Esta configuración se carga dinámicamente desde el backend o configuración.
/// </summary>
public class RequiredFieldsConfiguration
{
    /// <summary>
    /// Campos requeridos para crear una reserva
    /// Ejemplo: ["Service", "DesiredDate", "DesiredTime"]
    /// </summary>
    public List<string> CoreFields { get; set; } = new()
    {
        "Service",
        "DesiredDate",
        "DesiredTime"
    };

    /// <summary>
    /// Campos de identidad requeridos
    /// Ejemplo: ["CustomerName", "Phone"]
    /// </summary>
    public List<string> IdentityFields { get; set; } = new()
    {
        "CustomerName",
        "Phone"
    };

    /// <summary>
    /// Atributos específicos del negocio requeridos
    /// Ejemplo (Baby Spa): ["BabyAge", "BabyName"]
    /// Ejemplo (Restaurant): ["PartySize"]
    /// </summary>
    public List<string> BusinessAttributes { get; set; } = new();

    /// <summary>
    /// Campos opcionales que mejoran la experiencia pero no son obligatorios
    /// Ejemplo: ["Email", "SpecialRequests"]
    /// </summary>
    public List<string> OptionalFields { get; set; } = new();

    /// <summary>
    /// Obtiene todos los campos requeridos (core + identity + business attributes)
    /// </summary>
    public List<string> GetAllRequiredFields()
    {
        var allFields = new List<string>();
        allFields.AddRange(CoreFields);
        allFields.AddRange(IdentityFields);
        allFields.AddRange(BusinessAttributes);
        return allFields.Distinct().ToList();
    }
}
