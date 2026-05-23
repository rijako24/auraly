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

    /// <summary>Descripción técnica de qué hace la tool (no cuándo llamarla).</summary>
    string Description { get; }

    /// <summary>JSON Schema RFC 7159 de los parámetros.</summary>
    string ParametersSchema { get; }

    /// <summary>Plantilla por defecto si la tool renderiza output al cliente.</summary>
    string? DefaultTemplateId => null;

    /// <summary>Contenido de la plantilla por defecto.</summary>
    string? DefaultTemplate => null;

    /// <summary>
    /// Eventos semánticos universales que disparan esta tool.
    /// El compositor los traduce a un bloque "## CUÁNDO USAR {Name}" en el prompt.
    /// Ejemplos: "customer_frustration", "consecutive_errors", "out_of_scope_request".
    /// Tenants pueden filtrar / sobrescribir vía config.
    /// </summary>
    IReadOnlyList<string> SemanticTriggers => [];

    /// <summary>Invariantes universales evaluadas antes de ejecutar la tool.</summary>
    ToolAvailabilityResult Evaluate(AgentToolContext ctx, JsonElement arguments) =>
        new(true, null, null);

    /// <summary>
    /// Resuelve la clave de scope para verificaciones de esta tool.
    /// Null (default) = usa el resolver estándar basado en facts del contexto.
    /// Override cuando la tool necesita resolver el scope desde sus argumentos
    /// (ej. assign_paid_slot usa args.date/args.time en lugar de los facts del turno).
    /// </summary>
    Func<JsonElement, AgentToolContext, string?>? VerificationScopeResolver => null;

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
