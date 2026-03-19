namespace MimosBabySpa.Application.DTOs;

/// <summary>
/// Claves en <see cref="CreateReservationRequest.BusinessAttributes"/> interpretadas al crear la reserva.
/// Mantiene un solo lugar para el nombre del campo de extras.
/// </summary>
public static class ReservationBusinessAttributeKeys
{
    /// <summary>CSV de nombres de extras (flujo genérico y herramienta legacy).</summary>
    public const string SelectedAddOns = "selected_add_ons";

    private const string LegacySelectedAddOnsKey = "SelectedAddOns";

    /// <summary>
    /// Obtiene el CSV de add-ons desde atributos de negocio; normaliza "ninguno" a ausencia.
    /// Acepta <see cref="SelectedAddOns"/> (cualquier casing) o clave legacy PascalCase.
    /// </summary>
    public static string? GetSelectedAddOnsCsv(IReadOnlyDictionary<string, string> businessAttributes)
    {
        string? raw = null;
        foreach (var kv in businessAttributes)
        {
            if (string.Equals(kv.Key, SelectedAddOns, StringComparison.OrdinalIgnoreCase))
            {
                raw = kv.Value;
                break;
            }
        }

        if (raw == null)
        {
            foreach (var kv in businessAttributes)
            {
                if (string.Equals(kv.Key, LegacySelectedAddOnsKey, StringComparison.OrdinalIgnoreCase))
                {
                    raw = kv.Value;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        return string.Equals(raw, "ninguno", StringComparison.OrdinalIgnoreCase) ? null : raw;
    }
}
