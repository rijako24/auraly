namespace MimosBabySpa.Domain.Enums;

public enum BusinessConfigurationKey
{
    Persona = 0,                // PERSONA/IDENTIDAD (debe ir primero)
    Objective = 1,              // OBJETIVO
    GeneralInformation = 2,     // INFORMACIÓN GENERAL
    BusinessRules = 3,          // REGLAS DE NEGOCIO
    ContactInformation = 4,     // Información de contacto
    Location = 5,               // Ubicación
    OperatingHours = 6,         // Horarios de atención
    PaymentMethods = 7,         // Métodos de pago
    Policies = 8,               // Políticas del negocio
    ContextData = 9,            // Instrucciones de qué datos extraer del contexto (ej: "Extraer edad del niño", "Extraer qué plan se le puede recomendar")
    PlanRules = 10,             // Reglas para determinar planes según edad u otros criterios (formato JSON o texto estructurado)
    IntentRules = 11            // Reglas específicas para cada intención (formato JSON: {"Greeting": "...", "AskAge": "...", ...})
}
