namespace MimosBabySpa.Domain.Enums;

public enum SystemConfigurationKey
{
    ToneAndStyle = 1,           // TONO Y ESTILO (genérico para todos los negocios)
    DefaultGreeting = 2,
    DefaultFarewell = 3,
    MaxConversationHistory = 4,
    DefaultTemperature = 5,
    DefaultMaxTokens = 6,
    ContextExtractionPrompt = 7     // Prompt unificado para clasificación de intención y extracción de contexto (usa {intentPrompt}, {contextData}, {generalInfo}, {planRules})
}
