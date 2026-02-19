using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.StateManagement;

/// <summary>
/// ÚNICA fuente de verdad para aplicar campos y flags al estado de conversación.
///
/// Reglas centralizadas:
/// - Cambiar Service/DesiredDate/DesiredTime → resetea AvailabilityConfirmed y ReservationConfirmed.
/// - Los atributos se guardan vía SetAttribute (que ya gestiona Version y UpdatedAt).
/// - No hay doble incremento de Version.
/// - IsPhrase usa un conjunto estático para no recrear arrays en cada llamada.
/// </summary>
public class ConversationStateUpdater : IConversationStateUpdater
{
    private readonly ILogger<ConversationStateUpdater> _logger;

    // Patrones de frases completas — estáticos para evitar re-creación por llamada
    private static readonly HashSet<string> PhraseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "tengo", "tiene", "está", "son", "están", "quiero", "deseo", "necesito",
        "muy", "mucho", "poco", "algo", "nada"
    };

    private static readonly Regex PhraseArticlePattern =
        new(@"\b(el|la|los|las)\s+(bebé|niño|niña|hijo|hija)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ConversationStateUpdater(ILogger<ConversationStateUpdater> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────
    // ApplyField — campos de datos (core + atributos)
    // ─────────────────────────────────────────────────────────────────

    public ApplyFieldResult ApplyField(ConversationState state, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
            return Fail("field y value no pueden estar vacíos");

        if (IsPhrase(value))
        {
            _logger.LogDebug("Valor rechazado por ser frase: {Value}", value);
            return Fail($"El valor '{value}' parece ser una frase. Solo valores estructurados.");
        }

        // Atributos de negocio con prefijo "Attribute:"
        if (field.StartsWith("Attribute:", StringComparison.OrdinalIgnoreCase))
        {
            var attributeName = field["Attribute:".Length..];
            state.SetAttribute(attributeName, value);
            // SetAttribute ya hace Version++ y UpdatedAt — NO se repite aquí
            return Ok($"Atributo '{attributeName}' = '{value}'");
        }

        // Campos core
        var result = field switch
        {
            "CustomerName" => ApplyCustomerName(state, value),
            "Phone"        => Ok("Phone ya provisto por el canal; no se sobreescribe"),
            "Email"        => ApplyEmail(state, value),
            "Service"      => ApplyService(state, value),
            "DesiredDate"  => ApplyDesiredDate(state, value),
            "DesiredTime"  => ApplyDesiredTime(state, value),
            _              => Fail($"Campo '{field}' no reconocido. Usar prefijo 'Attribute:' para atributos.")
        };

        if (result.Success)
        {
            state.UpdatedAt = DateTime.UtcNow;
            state.Version++;
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────
    // ApplyConfirmationFlag — flags de estado transaccional
    // ─────────────────────────────────────────────────────────────────

    public ApplyFieldResult ApplyConfirmationFlag(ConversationState state, string flag, bool value, string? extraData = null)
    {
        switch (flag)
        {
            case "ReservationConfirmed":
                state.ReservationConfirmed = value;
                break;

            case "AvailabilityConfirmed":
                state.AvailabilityConfirmed = value;
                // Siempre almacenar slots cuando extraData está presente: horarios del día (value=true)
                // o alternativas cuando el solicitado no está disponible (value=false)
                if (!string.IsNullOrEmpty(extraData))
                    state.AvailableTimeSlots = extraData;
                else if (!value)
                    state.AvailableTimeSlots = null;
                break;

            case "AddOnsOffered":
                state.AddOnsOffered = value;
                break;

            default:
                return Fail($"Flag '{flag}' no reconocido.");
        }

        state.UpdatedAt = DateTime.UtcNow;
        state.Version++;
        return Ok($"Flag '{flag}' = {value}");
    }

    // ─────────────────────────────────────────────────────────────────
    // ResetTransactionalFlags — cancelación o cambio de intención
    // ─────────────────────────────────────────────────────────────────

    public void ResetTransactionalFlags(ConversationState state)
    {
        state.AvailabilityConfirmed = false;
        state.ReservationConfirmed = false;
        state.AddOnsOffered = false;
        state.AvailableTimeSlots = null;
        state.UpdatedAt = DateTime.UtcNow;
        state.Version++;
        _logger.LogInformation("Flags transaccionales reseteados (cancelación o cambio de intención)");
    }

    // ─────────────────────────────────────────────────────────────────
    // Aplicadores privados por campo
    // ─────────────────────────────────────────────────────────────────

    private static ApplyFieldResult ApplyCustomerName(ConversationState state, string value)
    {
        state.CustomerName = value;
        return Ok($"CustomerName = '{value}'");
    }

    private static ApplyFieldResult ApplyEmail(ConversationState state, string value)
    {
        if (!IsValidEmail(value))
            return Fail($"'{value}' no es un email válido");
        state.Email = value;
        return Ok($"Email = '{value}'");
    }

    private static ApplyFieldResult ApplyService(ConversationState state, string value)
    {
        var changed = state.Service != value;
        state.Service = value;
        if (changed)
        {
            state.AvailabilityConfirmed = false;
            state.ReservationConfirmed = false;
            state.AddOnsOffered = false;
            state.AvailableTimeSlots = null;
        }
        return Ok($"Service = '{value}'" + (changed ? " (disponibilidad y add-ons reseteados)" : ""));
    }

    private static ApplyFieldResult ApplyDesiredDate(ConversationState state, string value)
    {
        if (!DateOnly.TryParse(value, out var date))
            return Fail($"'{value}' no es fecha válida (formato: YYYY-MM-DD)");

        var changed = state.DesiredDate != date;
        state.DesiredDate = date;
        if (changed)
        {
            state.AvailabilityConfirmed = false;
            state.ReservationConfirmed = false;
            state.AvailableTimeSlots = null;
        }
        return Ok($"DesiredDate = '{date:yyyy-MM-dd}'" + (changed ? " (disponibilidad reseteada)" : ""));
    }

    private static ApplyFieldResult ApplyDesiredTime(ConversationState state, string value)
    {
        if (!TimeOnly.TryParse(value, out var time))
            return Fail($"'{value}' no es hora válida (formato: HH:MM)");

        var changed = state.DesiredTime != time;
        state.DesiredTime = time;
        if (changed)
        {
            state.AvailabilityConfirmed = false;
            state.ReservationConfirmed = false;
        }
        return Ok($"DesiredTime = '{time:HH:mm}'" + (changed ? " (disponibilidad reseteada)" : ""));
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers privados
    // ─────────────────────────────────────────────────────────────────

    private static bool IsPhrase(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 8) return true;

        var lower = value.ToLowerInvariant();
        foreach (var keyword in PhraseKeywords)
            if (Regex.IsMatch(lower, $@"\b{Regex.Escape(keyword)}\b"))
                return true;

        return PhraseArticlePattern.IsMatch(lower);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch { return false; }
    }

    private static ApplyFieldResult Ok(string msg) => new(true, msg);
    private static ApplyFieldResult Fail(string msg) => new(false, msg);
}
