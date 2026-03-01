namespace MimosBabySpa.Domain.Enums;

public enum SystemConfigurationKey
{
    ToneAndStyle = 1,                                // TONO Y ESTILO del agente conversacional (genérico para todos los negocios)
    HumanEscalationErrorThreshold = 2               // Errores consecutivos del orquestador para escalar a humano (string: "2", "3", etc.)
}
