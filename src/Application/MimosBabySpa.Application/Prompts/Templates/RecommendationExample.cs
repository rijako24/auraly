namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Template estático para el ejemplo de recomendación completa.
/// Usa placeholders que se reemplazan con datos dinámicos en runtime.
/// 
/// IMPORTANTE: Este es CONTENIDO, no lógica.
/// El provider solo carga este template y reemplaza los placeholders.
/// </summary>
public static class RecommendationExample
{
    public const string Template = @"
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
EJEMPLO DE RECOMENDACIÓN COMPLETA
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**Servicio de ejemplo:** {SERVICE_NAME}

**Estructura para recomendar:**

Cliente: ""¿Qué me recomiendas?""

Tú: ""Te recomendaría **{SERVICE_NAME}**.

[QUÉ ES]: (Lee la descripción del servicio arriba y resume en 1-2 oraciones)

[POR QUÉ]: (Conecta con la situación específica del cliente)

[QUÉ INCLUYE]: (Extrae los componentes de la descripción del servicio)

[BENEFICIOS]: (Extrae los beneficios más relevantes para el cliente)

[INFO PRÁCTICA]: {PRACTICAL_INFO}.
¿Te gustaría que verifique disponibilidad?""

**Instrucción:** Lee la descripción COMPLETA del servicio (en la sección
de servicios disponibles arriba), extrae la información relevante, y
personalízala para la situación específica del cliente.
";
}
