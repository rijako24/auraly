using System.Globalization;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve un nombre de servicio extraído por el LLM al nombre canónico en base de datos.
///
/// Estrategia de resolución (orden de prioridad):
///   1. Coincidencia exacta (case + accent insensitive).
///   2. El nombre extraído contiene el nombre canónico ("Plan Marineritos" ⊇ "Marineritos").
///   3. El nombre canónico contiene el nombre extraído ("Marineritos" ⊆ "Plan Marineritos").
///   4. Si no hay match → null (el caller decide qué hacer).
///
/// Usa CompareOptions.IgnoreNonSpace para tolerar acentos omitidos por el LLM
/// (e.g. "decoracion" matchea "Decoración").
/// </summary>
public class ServiceNameResolver
{
    private static readonly CompareInfo Cmp = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ServiceNameResolver> _logger;

    public ServiceNameResolver(IUnitOfWork unitOfWork, ILogger<ServiceNameResolver> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Devuelve el ServiceName canónico de la base de datos que mejor coincide con <paramref name="input"/>.
    /// Retorna null si no encuentra coincidencia.
    /// </summary>
    public async Task<string?> ResolveAsync(Guid businessId, string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var services = await _unitOfWork.Services.GetActiveByBusinessIdAsync(businessId);
        var serviceList = services.ToList();

        var normalized = input.Trim();

        var match = serviceList.FirstOrDefault(s =>
            Cmp.Compare(s.ServiceName, normalized, Opts) == 0);
        if (match != null) return match.ServiceName;

        match = serviceList.FirstOrDefault(s =>
            Cmp.IndexOf(normalized, s.ServiceName, Opts) >= 0);
        if (match != null)
        {
            _logger.LogInformation(
                "ServiceNameResolver: '{Input}' → '{Canonical}' (canonical contained in input)",
                input, match.ServiceName);
            return match.ServiceName;
        }

        match = serviceList.FirstOrDefault(s =>
            Cmp.IndexOf(s.ServiceName, normalized, Opts) >= 0);
        if (match != null)
        {
            _logger.LogInformation(
                "ServiceNameResolver: '{Input}' → '{Canonical}' (input contained in canonical)",
                input, match.ServiceName);
            return match.ServiceName;
        }

        _logger.LogWarning(
            "ServiceNameResolver: no match found for '{Input}' in business {BusinessId}",
            input, businessId);
        return null;
    }
}
