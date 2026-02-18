using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.FlowEngine;

/// <summary>
/// Implementación del motor determinístico de flujo transaccional.
///
/// 100% determinístico: misma entrada → misma salida.
/// Sin acceso a BD, LLM ni texto libre del usuario.
/// Registrado como Singleton (sin estado mutable propio).
/// </summary>
public class FlowEngine : IFlowEngine
{
    private readonly ILogger<FlowEngine> _logger;

    public FlowEngine(ILogger<FlowEngine> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────
    // Evaluate — evaluación completa
    // ─────────────────────────────────────────────────────────────────

    public FlowEvaluationResult Evaluate(ConversationState state, RequiredFieldsConfiguration requiredFields)
    {
        var missingFields = GetMissingFields(state, requiredFields);
        var totalRequired = requiredFields.GetAllRequiredFields().Count;
        var collected = totalRequired - missingFields.Count;

        var nextStage = DetermineNextStage(state);

        // ✅ Efecto secundario controlado: actualizar la etapa en el estado
        if (state.CurrentStage != nextStage)
            state.CurrentStage = nextStage;

        var result = new FlowEvaluationResult
        {
            CurrentStage            = nextStage,
            SuggestedNextStage      = nextStage,
            MissingFields           = missingFields,
            IsComplete              = missingFields.Count == 0,
            CompletenessPercentage  = totalRequired > 0 ? (int)((double)collected / totalRequired * 100) : 0,
            CanCheckAvailability    = CanCheckAvailability(state),
            CanCreateReservation    = CanCreateReservation(state),
            DiagnosticMessage       = BuildDiagnosticMessage(state, missingFields)
        };

        _logger.LogDebug(
            "FlowEngine → Stage={Stage}, Complete={Pct}%, CanCheck={CanCheck}, CanCreate={CanCreate}, Missing=[{Missing}]",
            result.CurrentStage, result.CompletenessPercentage,
            result.CanCheckAvailability, result.CanCreateReservation,
            string.Join(", ", missingFields));

        return result;
    }

    // ─────────────────────────────────────────────────────────────────
    // CanCheckAvailability — primera verificación (no re-verificación)
    // ─────────────────────────────────────────────────────────────────

    public bool CanCheckAvailability(ConversationState state)
    {
        if (string.IsNullOrWhiteSpace(state.Service))
        {
            _logger.LogDebug("CanCheckAvailability=false: falta Service");
            return false;
        }

        if (!state.DesiredDate.HasValue)
        {
            _logger.LogDebug("CanCheckAvailability=false: falta DesiredDate");
            return false;
        }

        if (state.AvailabilityConfirmed)
        {
            _logger.LogDebug("CanCheckAvailability=false: ya confirmada para {Service}/{Date}",
                state.Service, state.DesiredDate);
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    // ShouldRecheckAvailability — re-verificación explícita por usuario
    // ─────────────────────────────────────────────────────────────────

    public bool ShouldRecheckAvailability(ConversationState state)
    {
        return !string.IsNullOrWhiteSpace(state.Service) && state.DesiredDate.HasValue;
    }

    // ─────────────────────────────────────────────────────────────────
    // CanCreateReservation
    // ─────────────────────────────────────────────────────────────────

    public bool CanCreateReservation(ConversationState state)
    {
        if (state.ReservationCreated)
        {
            _logger.LogDebug("CanCreateReservation=false: ya creada");
            return false;
        }
        if (!state.ReservationConfirmed)
        {
            _logger.LogDebug("CanCreateReservation=false: falta confirmación del usuario");
            return false;
        }
        if (!state.AvailabilityConfirmed)
        {
            _logger.LogDebug("CanCreateReservation=false: disponibilidad no confirmada");
            return false;
        }
        if (string.IsNullOrWhiteSpace(state.Service))
        {
            _logger.LogDebug("CanCreateReservation=false: falta Service");
            return false;
        }
        if (!state.DesiredDate.HasValue)
        {
            _logger.LogDebug("CanCreateReservation=false: falta DesiredDate");
            return false;
        }
        if (!state.DesiredTime.HasValue)
        {
            _logger.LogDebug("CanCreateReservation=false: falta DesiredTime");
            return false;
        }

        _logger.LogInformation(
            "CanCreateReservation=true: Service={Service}, Date={Date}, Time={Time}",
            state.Service, state.DesiredDate, state.DesiredTime);

        return true;
    }

    // ─────────────────────────────────────────────────────────────────
    // GetMissingFields
    // ─────────────────────────────────────────────────────────────────

    public List<string> GetMissingFields(ConversationState state, RequiredFieldsConfiguration requiredFields)
    {
        var missing = new List<string>();

        foreach (var field in requiredFields.CoreFields)
            if (!IsFieldPresent(state, field)) missing.Add(field);

        foreach (var field in requiredFields.IdentityFields)
            if (!IsFieldPresent(state, field)) missing.Add(field);

        foreach (var attr in requiredFields.BusinessAttributes)
            if (!state.HasAttribute(attr)) missing.Add($"Attribute:{attr}");

        return missing;
    }

    // ─────────────────────────────────────────────────────────────────
    // DetermineNextStage — flujo lineal determinístico
    // ─────────────────────────────────────────────────────────────────

    public TransactionStage DetermineNextStage(ConversationState state)
    {
        if (state.ReservationCreated)
            return TransactionStage.BookingCompleted;

        if (state.AvailabilityConfirmed
            && !string.IsNullOrWhiteSpace(state.Service)
            && state.DesiredDate.HasValue
            && state.DesiredTime.HasValue)
            return TransactionStage.ConfirmingBooking;

        if (!string.IsNullOrWhiteSpace(state.Service) && state.DesiredDate.HasValue)
            return TransactionStage.CheckingAvailability;

        if (!string.IsNullOrWhiteSpace(state.Service))
            return TransactionStage.ExploringServices;

        return TransactionStage.CollectingInformation;
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers privados
    // ─────────────────────────────────────────────────────────────────

    private static bool IsFieldPresent(ConversationState state, string fieldName) => fieldName switch
    {
        "CustomerName"    => !string.IsNullOrWhiteSpace(state.CustomerName),
        "Phone"           => !string.IsNullOrWhiteSpace(state.Phone),
        "Email"           => !string.IsNullOrWhiteSpace(state.Email),
        "Service"         => !string.IsNullOrWhiteSpace(state.Service),
        "DesiredDate"     => state.DesiredDate.HasValue,
        "DesiredTime"     => state.DesiredTime.HasValue,
        _                 => false
    };

    private static string BuildDiagnosticMessage(ConversationState state, List<string> missing)
    {
        var parts = new List<string>();

        if (missing.Count == 0)
            parts.Add("✓ Todos los campos completos");
        else
            parts.Add($"⚠ Faltan: {string.Join(", ", missing)}");

        if (state.AvailabilityConfirmed)
            parts.Add("✓ Disponibilidad confirmada");

        if (state.ReservationConfirmed)
            parts.Add("✓ Usuario confirmó reserva");

        if (state.ReservationCreated)
            parts.Add($"✓ Reserva creada: {state.ReservationId}");

        return string.Join(" | ", parts);
    }
}
