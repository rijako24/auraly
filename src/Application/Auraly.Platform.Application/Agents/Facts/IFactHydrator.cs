using Auraly.Platform.Application.Agents.Configuration;

namespace Auraly.Platform.Application.Agents.Facts;

/// <summary>
/// Hidrata el diccionario de facts al inicio de cada turno con valores que
/// provienen de fuentes del sistema (canal, sesión, etc.) sin necesitar
/// que el LLM los solicite.
///
/// Solo escribe facts cuya entrada en FactSchema tenga source != "user"
/// y que aún no tengan valor.
/// Orquesta un conjunto de IFactSourceResolver registrados en DI.
/// </summary>
public interface IFactHydrator
{
    /// <summary>
    /// Lee el factSchema y escribe en facts los valores resolubles por el sistema.
    /// No modifica facts que ya tienen valor (el dato de usuario tiene precedencia).
    /// </summary>
    void Hydrate(
        IReadOnlyList<FactSchemaEntry> factSchema,
        Dictionary<string, string> facts,
        FactHydratorContext context);
}
