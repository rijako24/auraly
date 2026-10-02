using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Returns;

public static class SalesReturnReceipt
{
    public static OnlineSalesReceipt Create(SalesReturnDetail detail)
    {
        var lines = detail.Lines.Select(line => new OnlineSalesReceiptLine(
            string.Empty, line.Description, line.Quantity, line.UnitPrice,
            line.DiscountAmount, line.TaxAmount, line.LineTotal, line.TaxCode, line.TaxRate))
            .Concat((detail.Charges ?? []).Where(charge => charge.InvoicedAmount > 0)
                .Select(charge => new OnlineSalesReceiptLine(charge.Code, charge.Name, 1,
                    charge.InvoicedUntaxedAmount, 0, charge.InvoicedTaxAmount,
                    charge.InvoicedAmount, charge.TaxCode, charge.TaxRate))).ToArray();
        return new OnlineSalesReceipt(detail.ReturnId, SalesReturnDocumentTypes.SalesReturn,
            detail.DocumentNumber, null, detail.ReturnedAt, detail.CustomerIdentification,
            lines, [], detail.UntaxedAmount, detail.TaxAmount, detail.TotalAmount,
            null, null, detail.FiscalStatus, detail.CustomerName,
            CompanyName: detail.CompanyName, NetPayableAmount: detail.TotalAmount, PayableRoundingAmount: detail.RoundingAmount,
            SalesReturnPrintDetails: new(detail.OriginalDocumentNumber,
                detail.ReasonDescription, detail.EconomicResolution, detail.PrintTemplateVersion));
    }
}
