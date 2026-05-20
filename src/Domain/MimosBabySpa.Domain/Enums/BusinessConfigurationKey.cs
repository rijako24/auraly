namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Claves de configuración almacenadas en BusinessConfigurations.
///
/// Principio de diseño: solo pertenecen aquí las configuraciones de INFRAESTRUCTURA
/// que son hechos del negocio independientes de cualquier flow (credenciales externas,
/// conexiones, secrets). Todo comportamiento de flujo vive en el JSON del nodo.
/// </summary>
public enum BusinessConfigurationKey
{
    Integrations = 6,  // JSON: integraciones externas (Google Calendar, webhooks, etc.)
    UseAgenticOrchestrator = 7  // "true" para enrutar al motor agentico (Function Calling)
}
