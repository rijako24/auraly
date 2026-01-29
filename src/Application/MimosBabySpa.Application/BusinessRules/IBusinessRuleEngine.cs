using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.BusinessRules;

/// <summary>
/// Business Rule Engine - Motor de reglas de negocio.
/// 
/// Este componente encapsula TODAS las reglas de negocio específicas del dominio.
/// Es la ÚNICA autoridad para:
/// - Validar disponibilidad
/// - Asignar recursos
/// - Resolver conflictos
/// - Aplicar restricciones de negocio
/// 
/// El LLM y el FlowEngine NUNCA deben tomar decisiones de negocio.
/// Siempre deben consultar a este engine.
/// 
/// PRINCIPIOS:
/// - Las respuestas son ABSOLUTAS y NO negociables
/// - Encapsula toda la lógica compleja de negocio
/// - Es extensible y configurable por negocio
/// - Retorna resultados estructurados con razones claras
/// </summary>
public interface IBusinessRuleEngine
{
    /// <summary>
    /// Valida si una reserva puede ser creada según las reglas de negocio.
    /// Esto incluye validaciones que van más allá de la disponibilidad simple.
    /// </summary>
    /// <param name="businessId">ID del negocio</param>
    /// <param name="state">Estado de la conversación</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado de validación con razones</returns>
    Task<BusinessRuleValidationResult> ValidateReservationAsync(
        Guid businessId,
        ConversationState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determina si se aplican reglas especiales para este cliente/servicio.
    /// Ejemplo: descuentos, prioridades, restricciones específicas.
    /// </summary>
    Task<BusinessRuleContext> GetBusinessContextAsync(
        Guid businessId,
        string phone,
        string? service,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida si un atributo de negocio es válido según las reglas.
    /// Ejemplo: edad mínima/máxima, tamaño de grupo, etc.
    /// </summary>
    BusinessRuleValidationResult ValidateBusinessAttribute(
        Guid businessId,
        string attributeName,
        string attributeValue);
}
