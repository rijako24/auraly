using Auraly.Contracts.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Pos.Edge.Infrastructure;

public static class PosReceiptPrintMapping
{
    public static PosReceipt ToPosReceipt(this OnlineSalesReceipt receipt, Guid printJobId, int paperWidth) => new(
        printJobId, new DocumentId(receipt.DocumentId), receipt.DocumentNumber, receipt.FiscalNumber,
        receipt.IssuedAt, receipt.CustomerIdentification,
        receipt.Lines.Select(line => new PosReceiptLine(line.ProductCode, line.Description,
            line.Quantity, line.UnitPrice, line.Discount, line.Tax, line.Total, line.TaxCode, line.TaxRate, line.UnitCode)).ToArray(),
        receipt.Payments.Select(payment => new OfflineSalePayment(payment.MethodCode, payment.Amount,
            payment.Reference, payment.CardFranchiseCode, payment.ApprovalNumber, payment.BankAccountId,
            payment.Notes, payment.TenderedAmount, payment.RoundingAdjustment)).ToArray(),
        receipt.UntaxedAmount, receipt.TaxAmount, receipt.PayableAmount, receipt.Cufe, receipt.QrPayload,
        paperWidth, receipt.DocumentType, receipt.CompanyName, receipt.CompanyLogoSource,
        receipt.WithholdingTotal, receipt.NetPayableAmount, receipt.Withholdings, receipt.CustomerName,
        CreditAcknowledgement: receipt.CreditAcknowledgement, InvoicePrintDetails: receipt.InvoicePrintDetails,
        CustomerPhone: receipt.CustomerPhone, CustomerAddress: receipt.CustomerAddress,
        PayableRoundingAmount: receipt.PayableRoundingAmount);

    public static OnlineSalesReceipt ToPrintDocument(this PosReceipt receipt) => new(
        receipt.DocumentId.Value, receipt.DocumentType, receipt.DocumentNumber, receipt.FiscalNumber,
        receipt.IssuedAt, receipt.CustomerIdentification,
        receipt.Lines.Select(line => new OnlineSalesReceiptLine(line.ProductCode, line.Description,
            line.Quantity, line.UnitPrice, line.Discount, line.Tax, line.Total, line.TaxCode, line.TaxRate, line.UnitCode)).ToArray(),
        receipt.Payments.Select(payment => new OnlineSalesPayment(payment.MethodCode, payment.Amount,
            payment.Reference, payment.CardFranchiseCode, payment.ApprovalNumber, payment.BankAccountId,
            payment.Notes, payment.TenderedAmount, payment.RoundingAdjustment)).ToArray(),
        receipt.UntaxedAmount, receipt.TaxAmount, receipt.PayableAmount, receipt.Cufe, receipt.QrPayload, null,
        receipt.CustomerName ?? receipt.CustomerIdentification, receipt.CompanyName, receipt.CompanyLogoSource,
        receipt.WithholdingTotal, receipt.NetPayableAmount, receipt.Withholdings, receipt.CreditAcknowledgement,
        receipt.InvoicePrintDetails, receipt.CustomerPhone, receipt.CustomerAddress,
        PayableRoundingAmount: receipt.PayableRoundingAmount);
}
