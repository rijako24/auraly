using Auraly.Pos.Edge.Infrastructure;
using Auraly.Pos.Printing;

namespace Auraly.Pos.Edge.Host;

public sealed class PosPortfolioPaymentTicketPrinter(
    PosPrinterConfigurationStore configuration,
    IWindowsRenderedPrintJob renderedPrintJob,
    PortfolioPaymentReceiptRenderer renderer,
    PosWorkstationIdentity? workstation = null)
{
    public Task PrintAsync(PortfolioPaymentReceipt receipt, string? requestedFormat,
        CancellationToken cancellationToken)
    {
        var settings = configuration.LoadForPosPrinting();
        var format = requestedFormat ?? settings.PosOutputFormat;
        if (format is not (PrintTemplateFormats.Receipt or PrintTemplateFormats.HalfLetter or
                PrintTemplateFormats.HalfLegal or PrintTemplateFormats.Letter))
            throw new ArgumentException("El formato de impresión no es válido.", nameof(requestedFormat));
        var printerName = settings.InvoicePrinterForFormat(format);
        if ((format == PrintTemplateFormats.Receipt
                ? settings.ReceiptMode != PosPrinterModes.WindowsRaw
                : settings.OrderMode != OrderPrinterModes.WindowsPrint) ||
            string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException(
                "Configura la impresora de facturación para este formato antes de imprimir el comprobante.");

        var html = renderer.Render(receipt with
        {
            CompanyName = workstation?.CompanyName ?? receipt.CompanyName,
            LegalName = workstation?.CompanyLegalName ?? receipt.LegalName,
            Nit = workstation?.CompanyNit ?? receipt.Nit,
            VerificationDigit = workstation?.CompanyVerificationDigit ?? receipt.VerificationDigit,
            CompanyLogoSource = receipt.CompanyLogoSource is not null
                ? receipt.CompanyLogoSource
                : workstation?.PrintLogoSource,
            BusinessName = workstation?.BusinessName ?? receipt.BusinessName,
            BusinessAddress = workstation?.BusinessAddress ?? receipt.BusinessAddress,
            BusinessPhone = workstation?.BusinessPhone ?? receipt.BusinessPhone
        }, format, settings.ReceiptPaperWidthMillimeters);
        return renderedPrintJob.PrintAsync(
            printerName,
            $"{(receipt.Direction == "Receivable" ? "Abono-cartera" : "Pago-proveedor")}-{receipt.PaymentId:N}",
            html,
            configuration.ReceiptOutputDirectory,
            format == PrintTemplateFormats.Receipt ? settings.ReceiptPaperWidthMillimeters : null,
            cancellationToken);
    }
}
