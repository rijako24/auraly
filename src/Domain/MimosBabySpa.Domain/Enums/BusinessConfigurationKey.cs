namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Claves de configuración específicas del negocio.
/// Nota: BusinessInformation y ContextFieldsMapping fueron eliminados en favor de campos estructurados en Business.
/// </summary>
public enum BusinessConfigurationKey
{
    EntityExtractionConfig = 0,  // CONFIGURACIÓN DE EXTRACCIÓN DE ENTIDADES: Campos relevantes, descripciones y keywords para extracción genérica multi-tenant
    SalesGuidance = 3            // CONFIGURACIÓN DE GUÍA DE VENTAS: Atributos críticos y reglas antes de recomendar servicios
}
