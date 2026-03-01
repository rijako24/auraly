using Azure.AI.OpenAI;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Application.FlowEngine;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Interfaz para handlers de herramientas (tools) genéricas.
/// Todas las herramientas deben implementar esta interfaz para ser domain-agnostic.
/// </summary>
public interface IToolHandler
{
    /// <summary>
    /// Nombre de la función (debe ser único)
    /// </summary>
    string FunctionName { get; }

    /// <summary>
    /// Obtiene la definición de la función para el LLM
    /// </summary>
    FunctionDefinition GetDefinition();

    /// <summary>
    /// Ejecuta la herramienta con el contexto proporcionado
    /// </summary>
    /// <param name="context">Contexto de ejecución</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado de la ejecución</returns>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}

