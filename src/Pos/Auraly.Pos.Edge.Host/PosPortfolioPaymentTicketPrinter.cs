using Auraly.Pos.Edge.Infrastructure;
using Auraly.Pos.Printing;

namespace Auraly.Pos.Edge.Host;

public sealed class PosPortfolioPaymentTicketPrinter(
    PosPrinterConfigurationStore configuration,
    IWindowsRenderedPrintJob renderedPrintJob,
    PortfolioPaymentReceiptRenderer renderer,
    PosWorkstationIdentity? workstation = null)
{
    public Task PrintAsync(PortfolioPaymentReceipt receipt, CancellationToken cancellationToken)
    {
        var settings = configuration.LoadForPosPrinting();
        if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
            string.IsNullOrWhiteSpace(settings.PosPrinterName))
            throw new InvalidOperationException(
                "Configura la impresora de facturación en formato tirilla para imprimir este comprobante.");

        var html = renderer.Render(receipt with
        {
            CompanyName = workstation?.CompanyName ?? receipt.CompanyName,
            LegalName = workstation?.CompanyLegalName ?? receipt.LegalName,
            Nit = workstation?.CompanyNit ?? receipt.Nit,
            VerificationDigit = workstation?.CompanyVerificationDigit ?? receipt.VerificationDigit,
            CompanyLogoSource = workstation?.CompanyLogoSource ?? receipt.CompanyLogoSource,
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
