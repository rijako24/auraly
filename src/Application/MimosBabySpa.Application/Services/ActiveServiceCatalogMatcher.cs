using System.Globalization;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Coincidencia exacta de nombre de servicio activo (case y acentos insensibles).
/// Sin matching difuso: el LLM debe usar el nombre canónico del catálogo.
/// </summary>
public static class ActiveServiceCatalogMatcher
{
    private static readonly CompareInfo Cmp = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static string? MatchExact(IEnumerable<Service> services, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = input.Trim();
        var match = services.FirstOrDefault(s =>
            Cmp.Compare(s.ServiceName, normalized, Opts) == 0);

        return match?.ServiceName;
    }
}
