using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Operations.Catalog;

public sealed class GetServiceFulfillmentOperation : IAgentOperation
{
    public const string OperationId = "catalog.get_service_fulfillment";
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _names;

    public GetServiceFulfillmentOperation(IUnitOfWork unitOfWork, ServiceNameResolver names)
    {
        _unitOfWork = unitOfWork;
        _names = names;
    }

    public OperationDescriptor Descriptor { get; } = new(
        OperationId,
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": { "service": { "type": "string" } },
          "required": ["service"]
        }
        """,
        [
            "catalog.fulfillment_reservation",
            "catalog.fulfillment_enrollment",
            "catalog.fulfillment_missing_schedule",
            "catalog.service_not_found",
            "input.invalid"
        ],
        ["catalog.read"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(JsonElement input, OperationContext context, CancellationToken cancellationToken = default)
    {
        var requested = input.TryGetProperty("service", out var serviceElement) && serviceElement.ValueKind == JsonValueKind.String
            ? serviceElement.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(requested))
            return OperationOutcome.Fail("input.invalid", "service is required.", true);

        var canonical = await _names.ResolveAsync(context.BusinessId, requested, cancellationToken);
        if (canonical is null)
            return OperationOutcome.Fail("catalog.service_not_found", "Service was not found in the active catalog.", true);

        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(context.BusinessId, canonical);
        if (service is null)
            return OperationOutcome.Fail("catalog.service_not_found", "Service was not found in the active catalog.", true);

        var fixedSchedule = string.IsNullOrWhiteSpace(service.FixedScheduleLabel)
            ? null
            : service.FixedScheduleLabel.Trim();
        if (service.FulfillmentKind == ServiceFulfillmentKind.Enrollment && fixedSchedule is null)
        {
            return OperationOutcome.Fail(
                "catalog.fulfillment_missing_schedule",
                "Enrollment service has no configured fixed schedule.",
                true,
                "escalation.human",
                new { service = service.ServiceName });
        }

        var enrollment = service.FulfillmentKind == ServiceFulfillmentKind.Enrollment;
        return OperationOutcome.Ok(
            enrollment ? "catalog.fulfillment_enrollment" : "catalog.fulfillment_reservation",
            new
            {
                service = service.ServiceName,
                serviceCategory = service.ServiceCategory?.Name,
                fulfillmentReady = enrollment ? "enrollment" : "reservation",
                requiresAvailability = !enrollment,
                fixedScheduleLabel = fixedSchedule
            });
    }
}
