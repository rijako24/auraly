using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Retorna la ruta de cumplimiento de un servicio: reserva con disponibilidad
/// o inscripcion con horario fijo. Es una consulta estructurada y no modifica facts.
/// </summary>
public sealed class GetServiceFulfillmentTool : IAgentTool
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _nameResolver;

    public GetServiceFulfillmentTool(IUnitOfWork unitOfWork, ServiceNameResolver nameResolver)
    {
        _unitOfWork = unitOfWork;
        _nameResolver = nameResolver;
    }

    public string Name => "get_service_fulfillment";

    public string Description =>
        "Returns whether the selected service requires reservation availability or fixed-schedule enrollment.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": {
              "type": "string",
              "description": "Exact selected service name from the catalog. Optional when booking.service fact is already set."
            }
          }
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "service", out var serviceName))
            serviceName = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(serviceName))
            return ToolResultHelper.MissingPrerequisites(["service"]);

        var canonical = await _nameResolver.ResolveAsync(ctx.BusinessId, serviceName, cancellationToken);
        if (canonical is null)
        {
            return ToolResultHelper.Error(
                ToolErrorCodes.ServiceNotResolved,
                $"Service '{serviceName}' was not found in the catalog.",
                "Call get_service_catalog and use exactly one service name from the catalog.",
                recoverable: true);
        }

        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(ctx.BusinessId, canonical);
        if (service is null)
        {
            return ToolResultHelper.Error(
                ToolErrorCodes.ServiceNotResolved,
                $"Service '{canonical}' was not found in the catalog.",
                "Call get_service_catalog and use exactly one service name from the catalog.",
                recoverable: true);
        }

        var fulfillmentKind = service.FulfillmentKind == ServiceFulfillmentKind.Enrollment
            ? "enrollment"
            : "reservation";
        var fixedSchedule = NormalizeFixedScheduleLabel(service.FixedScheduleLabel);

        if (service.FulfillmentKind == ServiceFulfillmentKind.Enrollment
            && string.IsNullOrWhiteSpace(fixedSchedule))
        {
            return ToolResultHelper.Error(
                "service_fulfillment_missing_schedule",
                $"Service '{service.ServiceName}' is configured as enrollment but has no fixed schedule label.",
                "Escalate to a human or configure FixedScheduleLabel for this service.",
                recoverable: true);
        }

        var serviceCategory = service.ServiceCategory?.Name ?? string.Empty;
        var internalData = new
        {
            service = service.ServiceName,
            service_category = serviceCategory,
            fulfillment_ready = fulfillmentKind,
            requires_availability = fulfillmentKind == "reservation",
            fixed_schedule_label = fixedSchedule
        };

        var llmData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = service.ServiceName,
            ["service_category"] = serviceCategory
        };

        if (string.IsNullOrWhiteSpace(fixedSchedule))
        {
            llmData["guidance"] = "Continua con el siguiente dato necesario para revisar la agenda.";
        }
        else
        {
            llmData["official_schedule"] = fixedSchedule;
            llmData["guidance"] = "Continua con el horario oficial de inscripcion.";
        }

        return ToolResultHelper.OkWithLlm(internalData, llmData);
    }

    private static string? NormalizeFixedScheduleLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) ? null : label.Trim();
}
