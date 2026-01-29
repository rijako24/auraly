using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.FlowEngine;

/// <summary>
/// Implementación del Flow Engine - Cerebro determinístico del flujo transaccional.
/// 
/// Este componente es COMPLETAMENTE DETERMINÍSTICO y NO contiene lógica de negocio específica.
/// Todas las decisiones se basan ÚNICAMENTE en:
/// - Estado actual (ConversationState)
/// - Configuración de campos requeridos
/// - Flags de confirmación
/// 
/// NO tiene acceso a:
/// - Mensajes del usuario (texto)
/// - LLM
/// - Base de datos
/// - Servicios de negocio
/// </summary>
public class FlowEngine : IFlowEngine
{
    private readonly ILogger<FlowEngine> _logger;

    public FlowEngine(ILogger<FlowEngine> logger)
    {
        _logger = logger;
    }

    public FlowEvaluationResult Evaluate(
        ConversationState state,
        RequiredFieldsConfiguration requiredFields)
    {
        var result = new FlowEvaluationResult
        {
            CurrentStage = state.CurrentStage
        };

        // 1. Determinar campos faltantes
        result.MissingFields = GetMissingFields(state, requiredFields);
        var totalRequiredFields = requiredFields.GetAllRequiredFields().Count;
        var collectedFields = totalRequiredFields - result.MissingFields.Count;
        result.CompletenessPercentage = totalRequiredFields > 0 
            ? (int)((double)collectedFields / totalRequiredFields * 100) 
            : 0;
        result.IsComplete = result.MissingFields.Count == 0;

        // 2. Evaluar si se puede verificar disponibilidad
        result.CanCheckAvailability = CanCheckAvailability(state);

        // 3. Evaluar si se puede crear reserva
        result.CanCreateReservation = CanCreateReservation(state);

        // 4. Determinar siguiente etapa
        result.SuggestedNextStage = DetermineNextStage(state);

        // 5. Construir mensaje de diagnóstico
        result.DiagnosticMessage = BuildDiagnosticMessage(state, result);

        _logger.LogDebug(
            "FlowEngine Evaluation: Stage={Stage}, Complete={Complete}%, " +
            "CanCheckAvailability={CanCheck}, CanCreateReservation={CanCreate}, Missing={Missing}",
            result.CurrentStage, result.CompletenessPercentage, 
            result.CanCheckAvailability, result.CanCreateReservation, 
            string.Join(", ", result.MissingFields));

        return result;
    }

    public bool CanCheckAvailability(ConversationState state)
    {
        // REGLAS CRÍTICAS para verificar disponibilidad:
        // 1. DEBE tener servicio
        // 2. DEBE tener fecha
        // 3. La hora es opcional pero recomendada
        // NOTA: Permitimos re-verificar si el usuario lo solicita explícitamente (eso se maneja en el orquestador)

        if (string.IsNullOrWhiteSpace(state.Service))
        {
            _logger.LogDebug("No se puede verificar disponibilidad: falta Service");
            return false;
        }

        if (!state.DesiredDate.HasValue)
        {
            _logger.LogDebug("No se puede verificar disponibilidad: falta DesiredDate");
            return false;
        }

        // Permitir re-verificar si ya está confirmada PERO la fecha/hora cambió
        // (esto se manejará en el orquestador cuando UserRequestedAvailability = true)

        _logger.LogDebug("Se puede verificar disponibilidad: Service={Service}, Date={Date}, Time={Time}, AlreadyConfirmed={Confirmed}",
            state.Service, state.DesiredDate, state.DesiredTime?.ToString() ?? "not specified", state.AvailabilityConfirmed);

        return true;
    }

    public bool CanCreateReservation(ConversationState state)
    {
        // REGLAS CRÍTICAS para crear reserva:
        // 1. DEBE tener TODOS los datos transaccionales (Service, Date, Time)
        // 2. DEBE tener disponibilidad confirmada por el backend
        // 3. DEBE tener confirmación explícita del usuario (ReservationConfirmed)
        // 4. NO debe estar ya creada

        if (state.ReservationCreated)
        {
            _logger.LogDebug("La reserva ya fue creada anteriormente");
            return false;
        }

        if (!state.ReservationConfirmed)
        {
            _logger.LogDebug("No se puede crear reserva: falta confirmación explícita del usuario");
            return false;
        }

        if (!state.AvailabilityConfirmed)
        {
            _logger.LogDebug("No se puede crear reserva: disponibilidad no confirmada");
            return false;
        }

        if (string.IsNullOrWhiteSpace(state.Service))
        {
            _logger.LogDebug("No se puede crear reserva: falta Service");
            return false;
        }

        if (!state.DesiredDate.HasValue)
        {
            _logger.LogDebug("No se puede crear reserva: falta DesiredDate");
            return false;
        }

        if (!state.DesiredTime.HasValue)
        {
            _logger.LogDebug("No se puede crear reserva: falta DesiredTime");
            return false;
        }

        _logger.LogInformation(
            "Se puede crear reserva: todos los requisitos cumplidos " +
            "(Service={Service}, Date={Date}, Time={Time}, AvailabilityConfirmed={Availability}, " +
            "UserConfirmed={UserConfirmed})",
            state.Service, state.DesiredDate, state.DesiredTime, 
            state.AvailabilityConfirmed, state.ReservationConfirmed);

        return true;
    }

    public List<string> GetMissingFields(
        ConversationState state,
        RequiredFieldsConfiguration requiredFields)
    {
        var missingFields = new List<string>();

        // Verificar campos core
        foreach (var field in requiredFields.CoreFields)
        {
            if (!IsFieldPresent(state, field))
            {
                missingFields.Add(field);
            }
        }

        // Verificar campos de identidad
        foreach (var field in requiredFields.IdentityFields)
        {
            if (!IsFieldPresent(state, field))
            {
                missingFields.Add(field);
            }
        }

        // Verificar atributos de negocio
        foreach (var attribute in requiredFields.BusinessAttributes)
        {
            if (!state.HasAttribute(attribute))
            {
                missingFields.Add($"Attribute:{attribute}");
            }
        }

        return missingFields;
    }

    public TransactionStage DetermineNextStage(ConversationState state)
    {
        // Flujo lineal determinístico:
        // CollectingInformation → ExploringServices → CheckingAvailability → 
        // ConfirmingBooking → BookingCompleted

        // Si ya está completada, permanece en completed
        if (state.ReservationCreated)
        {
            return TransactionStage.BookingCompleted;
        }

        // Si tiene disponibilidad confirmada y todos los datos, debe confirmar
        if (state.AvailabilityConfirmed && 
            !string.IsNullOrWhiteSpace(state.Service) &&
            state.DesiredDate.HasValue &&
            state.DesiredTime.HasValue)
        {
            return TransactionStage.ConfirmingBooking;
        }

        // Si tiene servicio y fecha (con o sin hora), debe verificar disponibilidad
        if (!string.IsNullOrWhiteSpace(state.Service) && state.DesiredDate.HasValue)
        {
            return TransactionStage.CheckingAvailability;
        }

        // Si tiene servicio pero no fecha, está explorando opciones
        if (!string.IsNullOrWhiteSpace(state.Service))
        {
            return TransactionStage.ExploringServices;
        }

        // Si no tiene datos básicos, está recolectando información
        return TransactionStage.CollectingInformation;
    }

    // ========================================
    // MÉTODOS PRIVADOS HELPER
    // ========================================

    private bool IsFieldPresent(ConversationState state, string fieldName)
    {
        return fieldName switch
        {
            "CustomerName" => !string.IsNullOrWhiteSpace(state.CustomerName),
            "Phone" => !string.IsNullOrWhiteSpace(state.Phone),
            "Email" => !string.IsNullOrWhiteSpace(state.Email),
            "Service" => !string.IsNullOrWhiteSpace(state.Service),
            "DesiredDate" => state.DesiredDate.HasValue,
            "DesiredTime" => state.DesiredTime.HasValue,
            "DurationMinutes" => state.DurationMinutes.HasValue,
            _ => false
        };
    }

    private string BuildDiagnosticMessage(ConversationState state, FlowEvaluationResult result)
    {
        var messages = new List<string>();

        if (result.IsComplete)
        {
            messages.Add("✓ Todos los campos requeridos están completos");
        }
        else
        {
            messages.Add($"⚠ Faltan {result.MissingFields.Count} campo(s): {string.Join(", ", result.MissingFields)}");
        }

        if (state.AvailabilityConfirmed)
        {
            messages.Add("✓ Disponibilidad confirmada");
        }
        else if (result.CanCheckAvailability)
        {
            messages.Add("→ Listo para verificar disponibilidad");
        }

        if (state.ReservationConfirmed)
        {
            messages.Add("✓ Usuario confirmó reserva");
        }

        if (result.CanCreateReservation)
        {
            messages.Add("→ Listo para crear reserva");
        }

        if (state.ReservationCreated)
        {
            messages.Add($"✓ Reserva creada: {state.ReservationId}");
        }

        return string.Join(" | ", messages);
    }
}
