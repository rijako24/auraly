namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Claves de configuración almacenadas en BusinessConfigurations.
///
/// Solo pertenecen aquí configuraciones operativas del negocio independientes
/// del prompt del agente: integraciones, política de agendamiento
/// y política de reserva/anticipo.
/// PaymentConfirmationMessages (1) está obsoleto: usar messageSequences en Agents.SettingsJson.
/// </summary>
public enum BusinessConfigurationKey
{
    Integrations = 0,
    /// <summary>Obsoleto — reservado. Mensajes post-pago viven en Agents.SettingsJson → messageSequences.</summary>
    PaymentConfirmationMessages = 1,
    SchedulingPolicy = 2,
    BookingPolicy = 3
}
