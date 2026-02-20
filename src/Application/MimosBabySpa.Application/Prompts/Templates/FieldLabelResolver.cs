using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Resuelve nombres técnicos de campos a etiquetas legibles para prompts.
/// Fuente única: Core/Identity (etiquetas estándar) y BusinessAttributes (DisplayName desde EntityExtractionConfig).
/// Usado por ConfirmationSummaryBuilder y BuildStageInstruction para evitar nombres técnicos crudos al LLM.
/// </summary>
public static class FieldLabelResolver
{
    private static readonly IReadOnlyDictionary<string, string> CoreFieldLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Service"]      = "Servicio",
            ["DesiredDate"]  = "Fecha",
            ["DesiredTime"]  = "Hora",
            ["CustomerName"] = "Nombre del cliente",
            ["Phone"]        = "Teléfono",
            ["Email"]        = "Email"
        };

    /// <summary>
    /// Resuelve un fieldKey (ej. "CustomerName", "Attribute:BabyName") a su etiqueta legible.
    /// </summary>
    public static string Resolve(
        string fieldKey,
        IReadOnlyDictionary<string, AttributeDefinition> attributes)
    {
        if (fieldKey.StartsWith("Attribute:", StringComparison.OrdinalIgnoreCase))
        {
            var attrKey = fieldKey["Attribute:".Length..];
            var def = attributes.GetValueOrDefault(attrKey);
            return !string.IsNullOrWhiteSpace(def?.DisplayName) ? def.DisplayName : attrKey;
        }
        return CoreFieldLabels.TryGetValue(fieldKey, out var label) ? label : fieldKey;
    }
}
