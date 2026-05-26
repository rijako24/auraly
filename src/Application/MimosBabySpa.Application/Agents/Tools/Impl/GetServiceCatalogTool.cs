using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Packs.Booking;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Catálogo en dos modos:
///   - Sin <c>service</c>: todos los planes estándar activos (referencia para el LLM o template verbatim).
///   - Con <c>service</c>: add-ons compatibles con ese plan.
/// </summary>
public sealed class GetServiceCatalogTool : IAgentTool
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAddOnCatalogService _addOnCatalog;

    public GetServiceCatalogTool(
        IUnitOfWork unitOfWork,
        IAddOnCatalogService addOnCatalog)
    {
        _unitOfWork = unitOfWork;
        _addOnCatalog = addOnCatalog;
    }

    public string PackId => BookingPackIds.Booking;

    public string Name => "get_service_catalog";

    public string Description =>
        "Returns the business service catalog. Without service: all standard plans. " +
        "With service (exact plan name): compatible add-ons only.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": {
              "type": "string",
              "description": "Optional. Exact plan name. When set, returns add-ons for that plan only."
            }
          }
        }
        """;

    public async Task<string> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var ctx = invocation.Context;

        if (ToolResultHelper.TryGetString(invocation.Arguments, "service", out var serviceName))
            return await ReturnAddOnsAsync(ctx.BusinessId, serviceName, cancellationToken);

        return await ReturnAllPlansAsync(ctx.BusinessId, cancellationToken);
    }

    private async Task<string> ReturnAddOnsAsync(
        Guid businessId,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var compatible = await _addOnCatalog.GetCompatibleAsync(
            businessId, serviceName, cancellationToken);

        var addOns = compatible
            .Select(a => new
            {
                name = a.AddOnName,
                description = a.AddOnDescription,
                price = a.AddOnPrice
            })
            .ToList();

        return ToolResultHelper.Ok(new
        {
            service = serviceName,
            add_ons = addOns,
            template_id = "addons_compatible_list",
            template_data = new
            {
                service_name = serviceName,
                addons = addOns
            }
        });
    }

    private async Task<string> ReturnAllPlansAsync(
        Guid businessId,
        CancellationToken cancellationToken)
    {
        var services = await _unitOfWork.Services.GetByBusinessIdAsync(businessId);
        var serviceList = services
            .Where(s => s.IsActive && s.ServiceType == ServiceType.Standard)
            .OrderBy(s => s.ServiceName)
            .Select(s => new
            {
                name = s.ServiceName,
                description = s.Description,
                duration_minutes = s.DurationMinutes,
                price = s.Price
            })
            .ToList();

        return ToolResultHelper.Ok(new
        {
            services = serviceList,
            template_id = "service_catalog_summary",
            template_data = new
            {
                services = serviceList,
                currency = "COP"
            }
        });
    }
}
