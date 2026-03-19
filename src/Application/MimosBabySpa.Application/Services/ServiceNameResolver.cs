using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve un nombre de servicio extraído por el LLM al nombre canónico en base de datos.
///
/// Estrategia de resolución (orden de prioridad):
///   1. Coincidencia exacta (case-insensitive).
///   2. El nombre extraído contiene el nombre canónico ("Plan Marineritos" ⊇ "Marineritos").
///   3. El nombre canónico contiene el nombre extraído ("Marineritos" ⊆ "Plan Marineritos").
///   4. Si no hay match → null (el caller decide qué hacer).
///
/// Diseño: stateless, inyectable, sin caché — la tabla de servicios es pequeña y su lectura es barata.
/// </summary>
public class ServiceNameResolver
{
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

        // 1. Exact match (case-insensitive)
        var match = serviceList.FirstOrDefault(s =>
            string.Equals(s.ServiceName, normalized, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match.ServiceName;

        // 2. Extracted contains canonical ("Plan Marineritos" contains "Marineritos")
        match = serviceList.FirstOrDefault(s =>
            normalized.Contains(s.ServiceName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            _logger.LogInformation(
                "ServiceNameResolver: '{Input}' → '{Canonical}' (canonical contained in input)",
                input, match.ServiceName);
            return match.ServiceName;
        }

        // 3. Canonical contains extracted ("Marineritos" is contained in "Plan Marineritos")
        match = serviceList.FirstOrDefault(s =>
            s.ServiceName.Contains(normalized, StringComparison.OrdinalIgnoreCase));
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
