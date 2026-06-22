using System.Text.Json;
using MimosBabySpa.Application.Agents.Composition;

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

    /// <summary>
    /// Capacidades semanticas estables que implementa la tool.
    /// El motor debe depender de estas capacidades, no del nombre de function calling.
    /// </summary>
    IReadOnlyList<string> Capabilities => [];

    /// <summary>Descripción técnica de qué hace la tool (no cuándo llamarla).</summary>
    string Description { get; }

    /// <summary>JSON Schema RFC 7159 de los parámetros (fallback estático).</summary>
    string ParametersSchema { get; }

    /// <summary>
    /// JSON Schema contextual por tenant. Por defecto devuelve <see cref="ParametersSchema"/>.
    /// Tools como set_fact generan el contrato desde <see cref="AgentConfig.FactSchema"/>.
    /// </summary>
    string BuildParametersSchema(AgentConfig config) => ParametersSchema;

    /// <summary>Plantilla por defecto si la tool renderiza output al cliente.</summary>
    string? DefaultTemplateId => null;

    /// <summary>Contenido de la plantilla por defecto.</summary>
    string? DefaultTemplate => null;

    /// <summary>
    /// Eventos semánticos universales expuestos por la tool para capas de configuración o telemetría.
    /// La política conversacional debe venir de la configuración del tenant, por ejemplo globalActions o stages.
    /// Ejemplos: "customer_frustration", "consecutive_errors", "out_of_scope_request".
    /// </summary>
    IReadOnlyList<string> SemanticTriggers => [];

    /// <summary>Invariantes universales evaluadas antes de ejecutar la tool.</summary>
    ToolAvailabilityResult Evaluate(AgentToolContext ctx, JsonElement arguments) =>
        new(true, null, null);

    /// <summary>
    /// Resuelve los facts contra los que comparar el snapshot de una verificación al evaluar guards.
    /// Null (default) = usa <see cref="AgentToolContext.Facts"/>.
    /// Override cuando la tool valida inputs distintos a los facts del turno (p. ej. args date/time).
    /// </summary>
    Func<JsonElement, AgentToolContext, IReadOnlyDictionary<string, string>?>? VerificationDependencyResolver =>
        null;

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
