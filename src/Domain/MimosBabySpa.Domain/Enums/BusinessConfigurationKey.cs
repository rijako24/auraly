namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Claves de configuración específicas del negocio almacenadas en BusinessConfigurations.
/// El valor del enum coincide con el valor entero almacenado en la columna Key de la tabla.
/// </summary>
public enum BusinessConfigurationKey
{
    Personality = 0,            // Texto libre: identidad, tono y persona del asistente virtual
    EntityExtractionConfig = 1, // JSON: campos de extracción de entidades, descripciones y keywords
    SalesStrategy = 2           // Texto libre: instrucciones de recomendación y venta para el LLM
}
