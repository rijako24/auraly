using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve precios de servicio principal + add-ons desde el catálogo del negocio.
/// Fuente única de verdad para totales en flujo genérico (resumen, link de pago, reserva).
/// </summary>
public class ReservationPricingResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _nameResolver;
    private readonly ILogger<ReservationPricingResolver> _logger;

    public ReservationPricingResolver(
        IUnitOfWork unitOfWork,
        ServiceNameResolver nameResolver,
        ILogger<ReservationPricingResolver> logger)
    {
        _unitOfWork = unitOfWork;
        _nameResolver = nameResolver;
        _logger = logger;
    }

    /// <summary>
    /// Calcula precios. <paramref name="selectedAddOnsRaw"/> puede ser CSV o "ninguno".
    /// Si <paramref name="anticipoPercentage"/> está entre 1 y 100, calcula y formatea el anticipo
    /// con la misma regla que el resto de montos (único lugar de presentación de dinero).
    /// </summary>
    public async Task<ReservationPricingResult?> ResolveAsync(
        Guid businessId,
        string? serviceName,
        string? selectedAddOnsRaw,
        int? anticipoPercentage = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            _logger.LogWarning("ReservationPricingResolver: service name empty");
            return null;
        }

        var canonicalService = await _nameResolver.ResolveAsync(businessId, serviceName, ct) ?? serviceName.Trim();
        var mainService = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, canonicalService);
        if (mainService == null)
        {
            _logger.LogWarning(
                "ReservationPricingResolver: service '{Service}' not found for business {BusinessId}",
                canonicalService, businessId);
            return null;
        }

        decimal total = mainService.Price;
        var addonsSb = new StringBuilder();

        if (!IsNoAddOns(selectedAddOnsRaw))
        {
            foreach (var rawName in SplitAddOnNames(selectedAddOnsRaw))
            {
                var resolved = await _nameResolver.ResolveAsync(businessId, rawName, ct) ?? rawName.Trim();
                var addOnEntity = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, resolved);
                if (addOnEntity == null)
                {
                    _logger.LogWarning(
                        "ReservationPricingResolver: add-on '{Name}' not found — skipped",
                        rawName);
                    addonsSb.AppendLine($"- Extra: {rawName.Trim()} — (no encontrado en catálogo)");
                    continue;
                }

                total += addOnEntity.Price;
                addonsSb.AppendLine(
                    $"- Extra: {addOnEntity.ServiceName} — {FormatMoney(addOnEntity.Price)}");
            }
        }

        var addonsDetail = addonsSb.ToString().TrimEnd();

        string? anticipoDisplay = null;
        if (anticipoPercentage is > 0 and <= 100)
        {
            var anticipo = total * anticipoPercentage.Value / 100m;
            anticipoDisplay = FormatMoney(anticipo);
            _logger.LogDebug(
                "ReservationPricingResolver: anticipo {Pct}% of total {Total} = {Display}",
                anticipoPercentage.Value, total, anticipoDisplay);
        }

        return new ReservationPricingResult(
            ServicePrice: mainService.Price,
            Total: total,
            ServicePriceDisplay: FormatMoney(mainService.Price),
            AddOnsDetailDisplay: string.IsNullOrEmpty(addonsDetail) ? "—" : addonsDetail,
            TotalDisplay: FormatMoney(total),
            TotalInvariant: total.ToString(CultureInfo.InvariantCulture),
            AnticipoDisplay: anticipoDisplay);
    }

    /// <summary>
    /// Formato único de montos para UI del flujo (catálogo, totales, anticipo).
    /// Si en el futuro la moneda/cultura viene por negocio, se centraliza aquí.
    /// </summary>
    private static string FormatMoney(decimal amount) =>
        $"${amount.ToString("N0", CultureInfo.InvariantCulture)}";

    private static bool IsNoAddOns(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ||
        string.Equals(raw.Trim(), "ninguno", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitAddOnNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var part in raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, "ninguno", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return part;
        }
    }
}

/// <summary>
/// Resultado de <see cref="ReservationPricingResolver.ResolveAsync"/>.
/// </summary>
public sealed record ReservationPricingResult(
    decimal ServicePrice,
    decimal Total,
    string ServicePriceDisplay,
    string AddOnsDetailDisplay,
    string TotalDisplay,
    /// <summary>Total como string parseable (InvariantCulture) para montos y APIs.</summary>
    string TotalInvariant,
    /// <summary>Anticipo formateado; null si no se solicitó porcentaje o es 0.</summary>
    string? AnticipoDisplay);
