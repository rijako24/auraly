namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Campos requeridos para completar una reserva, derivados de la configuración del negocio.
/// </summary>
public class RequiredFieldsConfiguration
{
    /// <summary>Campos core siempre requeridos: Service, DesiredDate, DesiredTime.</summary>
    public List<string> CoreFields { get; set; } = [];

    /// <summary>Campos de identidad siempre requeridos: CustomerName, Phone.</summary>
    public List<string> IdentityFields { get; set; } = [];

    /// <summary>Atributos específicos del negocio que son requeridos (e.g. BabyAge).</summary>
    public List<string> BusinessAttributes { get; set; } = [];

    /// <summary>Campos opcionales (e.g. Email).</summary>
    public List<string> OptionalFields { get; set; } = [];

    /// <summary>Todos los campos requeridos combinados.</summary>
    public IEnumerable<string> AllRequiredFields =>
        CoreFields.Concat(IdentityFields).Concat(BusinessAttributes);
}
