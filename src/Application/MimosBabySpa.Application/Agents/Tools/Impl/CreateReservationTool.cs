using System.Text.Json;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Crea una reserva. Exige confirmación explícita del cliente (customerConfirmed=true)
/// y valida reglas de negocio antes de persistir.
///
/// Pre-condiciones no bypasseables (Capa 3):
///   - customerConfirmed debe ser true
///   - service, date, time, customer_name y customer_phone deben estar presentes
///   - IBusinessRuleEngine.ValidateReservationAsync debe pasar
/// </summary>
public sealed class CreateReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly IBusinessRuleEngine _rules;
    private readonly IConversationStateManager _stateManager;

    public CreateReservationTool(
        IReservationService reservations,
        IBusinessRuleEngine rules,
        IConversationStateManager stateManager)
    {
        _reservations = reservations;
        _rules = rules;
        _stateManager = stateManager;
    }

    public string Name => "create_reservation";

    public string Description =>
        "Creates a confirmed reservation. " +
        "IMPORTANT: Only call this after presenting a full summary to the customer and receiving explicit confirmation. " +
        "Set customer_confirmed=true only when the customer has said 'yes', 'confirm', or equivalent. " +
        "If customer_confirmed=false, this tool returns the summary for the customer to review.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": { "type": "string", "description": "Exact service name from catalog" },
            "date": { "type": "string", "description": "Date in YYYY-MM-DD format" },
            "time": { "type": "string", "description": "Time in HH:mm format" },
            "customer_name": { "type": "string", "description": "Customer's full name" },
            "customer_phone": { "type": "string", "description": "Customer's phone number" },
            "customer_email": { "type": "string", "description": "Customer's email (optional)" },
            "add_ons": { "type": "string", "description": "Comma-separated add-on names (optional)" },
            "customer_confirmed": {
              "type": "boolean",
              "description": "Must be true — only set after customer explicitly says yes to the summary"
            }
          },
          "required": ["service", "date", "time", "customer_name", "customer_phone", "customer_confirmed"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        // Validar presencia de campos requeridos
        var missing = new List<string>();
        if (!ToolResultHelper.TryGetString(arguments, "service", out var service)) missing.Add("service");
        if (!ToolResultHelper.TryGetString(arguments, "date", out var dateStr)) missing.Add("date");
        if (!ToolResultHelper.TryGetString(arguments, "time", out var timeStr)) missing.Add("time");
        if (!ToolResultHelper.TryGetString(arguments, "customer_name", out var customerName)) missing.Add("customer_name");
        if (!ToolResultHelper.TryGetString(arguments, "customer_phone", out var customerPhone)) missing.Add("customer_phone");

        if (missing.Count > 0)
            return ToolResultHelper.MissingPrerequisites([.. missing]);

        // Validar formatos de fecha/hora
        if (!DateOnly.TryParse(dateStr, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.", "Use YYYY-MM-DD.");

        if (date < DateOnly.FromDateTime(DateTime.UtcNow))
            return ToolResultHelper.Error("past_date", "Reservation date must be today or in the future.");

        if (!TimeOnly.TryParse(timeStr, out var time))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.", "Use HH:mm.");

        ToolResultHelper.TryGetString(arguments, "customer_email", out var customerEmail);
        ToolResultHelper.TryGetString(arguments, "add_ons", out var addOns);

        // Guardrail: confirmación explícita del cliente (Capa 3)
        if (!ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var confirmed) || !confirmed)
        {
            return ToolResultHelper.Ok(new
            {
                status = "pending_confirmation",
                summary = new
                {
                    service,
                    date = dateStr,
                    time = timeStr,
                    customer_name = customerName,
                    customer_phone = customerPhone,
                    add_ons = string.IsNullOrWhiteSpace(addOns) ? null : addOns,
                    message = "Please present this summary to the customer and ask for explicit confirmation before calling create_reservation again with customer_confirmed=true."
                }
            });
        }

        // Construir estado de conversación para el motor de reglas
        var state = await _stateManager.GetOrCreateStateAsync(
            ctx.ConversationId, ctx.BusinessId, customerPhone, cancellationToken);

        state.Service = service;
        state.DesiredDate = date;
        state.DesiredTime = time;
        state.CustomerName = customerName;
        state.Phone = customerPhone;
        if (!string.IsNullOrWhiteSpace(customerEmail)) state.Email = customerEmail;
        if (!string.IsNullOrWhiteSpace(addOns)) state.Attributes["SelectedAddOns"] = addOns;
        state.ReservationConfirmed = true;

        // Guardrail: reglas de negocio (Capa 3, antes era código muerto)
        var ruleResult = await _rules.ValidateReservationAsync(ctx.BusinessId, state, cancellationToken);
        if (!ruleResult.IsValid)
        {
            return ToolResultHelper.Error("business_rule_violation",
                ruleResult.Reason ?? "Business rules prevent this reservation.",
                ruleResult.Warnings.Count > 0 ? string.Join("; ", ruleResult.Warnings) : null);
        }

        var attributes = string.IsNullOrWhiteSpace(addOns)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["SelectedAddOns"] = addOns };

        var response = await _reservations.CreateReservationAsync(
            new CreateReservationRequest(
                ctx.BusinessId, ctx.ConversationId,
                service, date, time,
                customerName, customerEmail,
                customerPhone, attributes),
            cancellationToken);

        // Actualizar estado
        state.ReservationCreated = true;
        state.ReservationId = response.ReservationId;
        await _stateManager.SaveStateAsync(ctx.ConversationId, state, cancellationToken);

        return ToolResultHelper.Ok(new
        {
            reservation_id = response.ReservationId,
            service = response.ServiceName,
            employee = response.EmployeeName,
            date = response.Date.ToString("yyyy-MM-dd"),
            time = response.Time.ToString("HH:mm"),
            duration_minutes = response.DurationMinutes,
            add_ons = response.AddOnNames
        });
    }
}
