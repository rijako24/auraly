using Auraly.Pos.Edge.Infrastructure;
using Auraly.Pos.Printing;

namespace Auraly.Pos.Edge.Host;

public sealed class PosPortfolioPaymentTicketPrinter(
    PosPrinterConfigurationStore configuration,
    IWindowsRenderedPrintJob renderedPrintJob,
    PortfolioPaymentReceiptRenderer renderer,
    PosWorkstationIdentity? workstation = null,
    PosLocalPrintBrandingStore? logos = null,
    ILogger<PosPortfolioPaymentTicketPrinter>? logger = null)
{
    public Task PrintAsync(PortfolioPaymentReceipt receipt, CancellationToken cancellationToken)
    {
        var settings = configuration.LoadForPosPrinting();
        if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
            string.IsNullOrWhiteSpace(settings.PosPrinterName))
            throw new InvalidOperationException(
                "Configura la impresora de facturación en formato tirilla para imprimir este comprobante.");

        if (receipt.TenantId is { } tenantId && logos is not null &&
            !logos.AllowsTenant(tenantId))
            throw new InvalidOperationException("El comprobante pertenece a otra empresa.");
        string? localLogo = null;
        if (receipt.TenantId is { } selectedTenant)
        {
            try { localLogo = logos?.Get(selectedTenant); }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(exception,
                    "No se pudo leer el logo local del tenant {TenantId}; se imprimirá el nombre.",
                    selectedTenant);
            }
        }
        var html = renderer.Render(receipt with
        {
            CompanyName = workstation?.CompanyName ?? receipt.CompanyName,
            LegalName = workstation?.CompanyLegalName ?? receipt.LegalName,
            Nit = workstation?.CompanyNit ?? receipt.Nit,
            VerificationDigit = workstation?.CompanyVerificationDigit ?? receipt.VerificationDigit,
            CompanyLogoSource = receipt.TenantId is not null && logos is not null
                ? localLogo
                : workstation?.CompanyLogoSource ?? receipt.CompanyLogoSource,
            BusinessName = workstation?.BusinessName ?? receipt.BusinessName,
            BusinessAddress = workstation?.BusinessAddress ?? receipt.BusinessAddress,
            BusinessPhone = workstation?.BusinessPhone ?? receipt.BusinessPhone
        }, settings.ReceiptPaperWidthMillimeters);
        return renderedPrintJob.PrintAsync(
            settings.PosPrinterName,
            $"{(receipt.Direction == "Receivable" ? "Abono-cartera" : "Pago-proveedor")}-{receipt.PaymentId:N}",
            html,
            configuration.ReceiptOutputDirectory,
            settings.ReceiptPaperWidthMillimeters,
            cancellationToken);
    }
}
