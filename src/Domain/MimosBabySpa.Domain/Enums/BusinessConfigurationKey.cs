namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Claves de configuración almacenadas en BusinessConfigurations.
///
/// Solo pertenecen aquí configuraciones operativas del negocio independientes
/// del prompt del agente: integraciones, mensajes de pago, política de agendamiento
/// y política de reserva/anticipo.
/// </summary>
public enum BusinessConfigurationKey
{
    Integrations = 0,
    PaymentConfirmationMessages = 1,
    SchedulingPolicy = 2,
    BookingPolicy = 3
}
