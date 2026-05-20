using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Tools;

/// <summary>
/// Contrato de una tool que el LLM puede invocar vía Function Calling.
/// Cada implementación es un wrapper delgado sobre un servicio de dominio existente.
/// La validación de negocio ocurre dentro de ExecuteAsync — no en el LLM.
/// </summary>
public interface IAgentTool
{
    /// <summary>Nombre de la función tal como aparece en OpenAI (snake_case).</summary>
    string Name { get; }

    /// <summary>Descripción que el LLM usa para decidir cuándo llamar la tool.</summary>
    string Description { get; }

    /// <summary>JSON Schema RFC 7159 de los parámetros.</summary>
    string ParametersSchema { get; }

    /// <summary>
    /// Ejecuta la tool. Siempre retorna un JSON serializable con shape:
    /// { "ok": true, "data": {...} }
    /// o
    /// { "ok": false, "error": { "code": "...", "message": "...", "hint": "..." } }
    /// </summary>
    Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default);
}
