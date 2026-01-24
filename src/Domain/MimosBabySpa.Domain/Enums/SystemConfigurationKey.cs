namespace MimosBabySpa.Domain.Enums;

public enum SystemConfigurationKey
{
    ToneAndStyle = 1,                          // TONO Y ESTILO del agente conversacional (genérico para todos los negocios)
    AvailabilityContextTemplate = 2,            // Template para contexto de disponibilidad inyectado al LLM
    IntentDetectionContextTemplate = 3          // Template para contexto de intención detectada inyectado al LLM
}
