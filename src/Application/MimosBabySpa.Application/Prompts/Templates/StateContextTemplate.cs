namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Template para presentar el estado actual de la conversación.
/// El backend solo popula los placeholders con datos dinámicos.
/// </summary>
public static class StateContextTemplate
{
    /// <summary>
    /// Formato del encabezado del estado
    /// </summary>
    public const string Header = @"# ESTADO ACTUAL DE LA CONVERSACIÓN";

    /// <summary>
    /// Formato de la sección de completitud
    /// Placeholders: {completeness_percentage}
    /// </summary>
    public const string CompletenessSection = @"
## Completitud: {completeness_percentage}%";

    /// <summary>
    /// Formato de la sección de información recolectada
    /// Placeholders: {customer_name}, {phone}, {email}, {service}, {desired_date}, {desired_time}, 
    ///               {availability_confirmed}, {reservation_confirmed}, {reservation_created}
    /// </summary>
    public const string InformationSection = @"
## Información recolectada:
- Nombre del cliente: {customer_name}
- Teléfono: {phone}
- Email: {email}
- Servicio: {service}
- Fecha deseada: {desired_date}
- Hora deseada: {desired_time}
- Disponibilidad confirmada: {availability_confirmed}
- Reserva confirmada por usuario: {reservation_confirmed}
- Reserva creada: {reservation_created}";

    /// <summary>
    /// Formato de la sección de atributos adicionales
    /// Placeholders: {attributes_list}
    /// </summary>
    public const string AttributesSection = @"
## Información adicional del cliente:
{attributes_list}";

    /// <summary>
    /// Formato de la sección de campos faltantes
    /// Placeholders: {missing_fields_list}
    /// </summary>
    public const string MissingFieldsSection = @"
## Campos faltantes:
{missing_fields_list}";

    /// <summary>
    /// Formato de la sección de estado del flujo
    /// Placeholders: {diagnostic_message}
    /// </summary>
    public const string FlowStateSection = @"
## Estado del flujo:
{diagnostic_message}";
}
