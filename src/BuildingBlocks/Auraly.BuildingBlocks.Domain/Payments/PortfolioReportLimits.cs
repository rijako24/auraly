namespace Auraly.BuildingBlocks.Domain.Payments;

public static class PortfolioReportLimits
{
    public const int MaximumExportRows = 5_000;

    public static void ValidateExport(long documentRows, long applicationRows)
    {
        if (documentRows + applicationRows > MaximumExportRows)
            throw new PortfolioReportLimitException(
                "El informe supera 5.000 filas. Ajusta los filtros o desmarca el detalle de abonos/pagos antes de imprimir o exportar.");
    }
}

public sealed class PortfolioReportLimitException(string message) : Exception(message);
