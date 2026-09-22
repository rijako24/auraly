using System.Globalization;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

/// <summary>
/// Pure projection from an immutable issued-document snapshot to its shared presentation model.
/// It performs no business validation and reads no operational state.
/// </summary>
public static class SalesInvoicePresentationMapper
{
    public static OnlineSalesReceipt FromSnapshot(
        string documentType, string snapshotJson, string? fiscalStatus = null) =>
        documentType switch
        {
            PosSaleDocumentTypes.Invoice =>
                From(PosSaleContractSerializer.Deserialize(snapshotJson), fiscalStatus),
            ServiceInvoiceDocumentTypes.ServiceInvoice =>
                From(ServiceInvoiceSnapshotSerializer.Deserialize(snapshotJson), fiscalStatus),
            _ => throw new InvalidOperationException(
                $"Document type '{documentType}' has no invoice presentation mapper.")
        };

    public static OnlineSalesReceipt From(
        PosSaleUploadRequest request, string? fiscalStatus)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request.CommercialSnapshot;
        var customerName = request.UblSnapshot?.Customer.RegistrationName
            ?? snapshot.CustomerName
            ?? "Consumidor final";
        var ublLines = request.UblSnapshot?.Lines
            .ToDictionary(line => line.LineNumber)
            ?? [];
        return Create(
            request.DocumentId, snapshot.DocumentType, request.DocumentNumber.FullNumber,
            request.FiscalSnapshot?.FiscalNumber, snapshot, request.FiscalSnapshot,
            request.Lines.Select(line => new OnlineSalesReceiptLine(
                ublLines.GetValueOrDefault(line.LineNumber)?.ProductCode ?? line.ProductCodeSnapshot,
                line.Description, line.Quantity, line.UnitPrice, line.DiscountAmount,
                line.TaxAmount, line.LineTotal, line.TaxCode, line.TaxRate,
                ublLines.GetValueOrDefault(line.LineNumber)?.UnitCode ?? "EA"))
                .Concat((request.Charges ?? []).Where(charge => charge.InvoicedAmount > 0)
                    .Select(charge => new OnlineSalesReceiptLine(
                        charge.Code, charge.Name, 1, charge.InvoicedUntaxedAmount, 0,
                        charge.InvoicedTaxAmount, charge.InvoicedAmount,
                        charge.TaxCode, charge.TaxRate))).ToArray(),
            ComposePayments(request.Payments.Select(Payment), request.Credit?.Amount,
                request.Credit?.DueDate.ToString("O", CultureInfo.InvariantCulture)),
            request.UblSnapshot, customerName, fiscalStatus,
            request.Credit is null ? null : new CreditSaleAcknowledgement(
                request.DocumentId, request.DocumentNumber.FullNumber, snapshot.IssuedAt,
                customerName, snapshot.CustomerIdentification, request.Credit.Amount,
                request.Credit.RemainingCredit, request.Credit.SoldByName ?? "Usuario"));
    }

    public static OnlineSalesReceipt From(
        ServiceInvoiceSnapshot request, string? fiscalStatus)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payment = Payment(request.Payment);
        var collected = request.Payment.CollectedAmount;
        // Older service snapshots predate the explicit credit member; their frozen
        // payment and net total still provide the original financed balance.
        var credit = request.Credit?.Amount ??
            request.CommercialSnapshot.NetPayableAmount - collected;
        var creditReference = request.Credit?.DueDate.ToString("O", CultureInfo.InvariantCulture)
            ?? request.UblSnapshot.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var payments = collected > 0 ? new[] { payment } : [];
        return Create(
            request.DocumentId, request.CommercialSnapshot.DocumentType,
            request.DocumentNumber.FullNumber, request.FiscalSnapshot.FiscalNumber,
            request.CommercialSnapshot, request.FiscalSnapshot,
            request.Lines.Select(line => new OnlineSalesReceiptLine(
                line.ServiceCode, line.Description, line.Quantity, line.UnitPrice,
                line.DiscountAmount, line.TaxAmount, line.LineTotal, line.TaxCode,
                line.TaxRate, line.UnitCode)).ToArray(),
            ComposePayments(payments, credit > 0 ? credit : null, creditReference),
            request.UblSnapshot, request.UblSnapshot.Customer.RegistrationName,
            fiscalStatus, null);
    }

    private static OnlineSalesReceipt Create(
        Guid documentId, string documentType, string documentNumber, string? fiscalNumber,
        PosSaleCommercialSnapshotContract commercial, PosSaleFiscalSnapshotContract? fiscal,
        IReadOnlyList<OnlineSalesReceiptLine> lines,
        IReadOnlyList<OnlineSalesPayment> payments, PosSaleUblSnapshotContract? ubl,
        string customerName, string? fiscalStatus,
        CreditSaleAcknowledgement? creditAcknowledgement) => new(
            documentId, documentType, documentNumber, fiscalNumber, commercial.IssuedAt,
            commercial.CustomerIdentification, lines, payments, commercial.UntaxedAmount,
            commercial.TaxAmount, commercial.PayableAmount, fiscal?.Cufe, fiscal?.QrPayload,
            fiscalStatus, customerName,
            WithholdingTotal: commercial.Withholding?.WithholdingTotal ?? 0m,
            NetPayableAmount: commercial.NetPayableAmount,
            Withholdings: commercial.Withholding?.Lines,
            CreditAcknowledgement: creditAcknowledgement,
            InvoicePrintDetails: PrintDetails(ubl),
            CustomerPhone: ubl?.Customer.Telephone,
            CustomerAddress: ubl?.Customer.Address.AddressLine,
            PayableRoundingAmount: commercial.PayableRoundingAmount);

    private static OnlineSalesPayment Payment(PosSalePaymentContract payment) => new(
        payment.MethodCode, payment.Amount, payment.Reference, payment.CardFranchiseCode,
        payment.ApprovalNumber, payment.BankAccountId, payment.Notes,
        payment.TenderedAmount, payment.RoundingAdjustment);

    private static IReadOnlyList<OnlineSalesPayment> ComposePayments(
        IEnumerable<OnlineSalesPayment> payments, decimal? creditAmount,
        string? creditReference) =>
        creditAmount is { } amount
            ? payments.Append(new OnlineSalesPayment("Credit", amount, creditReference)).ToArray()
            : payments.ToArray();

    private static SalesInvoicePrintDetails? PrintDetails(
        PosSaleUblSnapshotContract? snapshot) => snapshot is null
        ? null
        : new SalesInvoicePrintDetails(
            snapshot.Supplier.RegistrationName, snapshot.Supplier.Identification,
            snapshot.Supplier.TaxResponsibilityCode, snapshot.Supplier.Address.AddressLine,
            snapshot.Customer.Address.AddressLine, snapshot.Authorization.Number,
            snapshot.Authorization.ValidFrom, snapshot.Authorization.ValidUntil,
            snapshot.Authorization.Prefix, snapshot.Authorization.RangeStart,
            snapshot.Authorization.RangeEnd, snapshot.PaymentFormCode,
            snapshot.PaymentMeansCode, snapshot.DueDate,
            snapshot.Supplier.Identification, "Auraly");
}
