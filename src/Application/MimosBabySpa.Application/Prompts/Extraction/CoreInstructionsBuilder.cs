using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Constructor de instrucciones core para extracción.
/// Contiene las reglas generales y contexto básico.
/// </summary>
public class CoreInstructionsBuilder
{
    /// <summary>
    /// Construye las instrucciones principales del prompt de extracción.
    /// </summary>
    public string Build(LoadedBusinessContext context, string userMessage)
    {
        var now = DateTime.Now;

        return $@"# EXTRACCIÓN DE INFORMACIÓN - JSON MODE

## CONTEXTO:

Negocio: {context.Info.Name}
Fecha: {now:yyyy-MM-dd}

## MENSAJE:
""{userMessage}""

---

## IMPORTANTE:
Responde SOLO con JSON válido sin texto adicional.
Diferencia claramente CustomerName (quien reserva) de Attribute:BabyName (nombre del bebé).";
    }
}
