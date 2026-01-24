namespace MimosBabySpa.Domain.Enums;

public enum BusinessConfigurationKey
{
    BusinessInformation = 0,    // INFORMACIÓN COMPLETA DEL NEGOCIO: Persona, horarios, servicios, duraciones, reglas de planes, comportamiento del asesor, herramientas disponibles, TODO
    ContextFieldsMapping = 1   // MAPEO DE CAMPOS DEL CONTEXTO: Información específica del negocio sobre qué campos guardar en el contexto y cómo detectarlos (ej: campos personalizados, reglas de detección, conversiones, etc.)
}
